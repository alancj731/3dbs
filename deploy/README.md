# Deploying SQL Practice

```
Browser ──► Vercel (Angular app)
              │  /api/*  (rewrite in web/vercel.json)
              ▼
            VM :443 ──► Caddy (HTTPS) ──► .NET API ──► SQL Server / MySQL / Postgres
                                                         (internal Docker network only)
```

Only Caddy is reachable from the internet. The databases have no published ports.

## 1. The VM

- **x86-64** (SQL Server does not run on ARM), **4 GB RAM** minimum, 2 vCPU, 20+ GB disk.
- Ubuntu 24.04 LTS or Debian 12.
- A hostname pointing at the VM: a DNS **A record** such as `api.example.com`, or the free
  `<ip-with-dashes>.sslip.io` name (for 203.0.113.10 that's `203-0-113-10.sslip.io`).

## 2. Prepare the server (once)

```bash
# Docker
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER && newgrp docker

# Firewall: SSH + HTTP(S) only
sudo ufw allow OpenSSH && sudo ufw allow 80,443/tcp && sudo ufw allow 443/udp && sudo ufw enable

# 2 GB swap: headroom for building the API image on a 4 GB VM
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

## 3. Deploy the backend

```bash
git clone https://github.com/alancj731/3dbs.git && cd 3dbs/deploy
cp .env.example .env
nano .env            # set API_DOMAIN, WEB_ORIGIN, DB_ADMIN_PASSWORD, PRACTICE_PASSWORD
./deploy.sh
```

On a first run, `deploy.sh`:
1. checks `.env`;
2. downloads AdventureWorksLT and starts the 3 databases;
3. restores the backup into SQL Server;
4. creates the restricted `practice` login;
5. builds and starts the API, which copies the data into MySQL and Postgres on first start;
6. starts Caddy, which gets a Let's Encrypt certificate.

Check it: `https://<API_DOMAIN>/api/exercises` should return JSON.

## 4. Deploy the frontend on Vercel

1. Edit `web/src/environments/environment.prod.ts` and set `apiBase` to `https://<API_DOMAIN>`. Commit and push.
2. On vercel.com: **Add New → Project**, import the GitHub repo, set **Root Directory** to `web`.
   Vercel picks up the build command and output directory from `vercel.json`.
3. Deploy. The browser calls the API on the VM directly, so `WEB_ORIGIN` in `deploy/.env` must be the
   Vercel site's address (e.g. `https://my-app.vercel.app`); the API only allows browser calls from that origin.
   Caddy sets `X-Real-IP` to the visitor's real address, which the per-visitor rate limit uses.

## Day-to-day

| Task | Command (in `deploy/`) |
|---|---|
| Pull new code and redeploy | `./deploy.sh update` |
| Redeploy current code | `./deploy.sh` |
| Status and memory use | `./deploy.sh status` |
| Follow API/Caddy logs | `./deploy.sh logs` |
| Stop everything | `docker compose --env-file .env down` |
| Wipe all data and start over | `docker compose --env-file .env down -v`, then `./deploy.sh` |

## Production settings vs local dev

- **SQL Server Express** edition: free for production (Developer edition is not); memory capped at 1.5 GB.
- MySQL runs with a 64 MB buffer pool and without `performance_schema`, which saves about 250 MB.
- Passwords live only in `deploy/.env` on the server (git-ignored). The committed defaults are for local dev only.
- Visitors' SQL is limited to 60 Run/Check requests per minute per visitor (`SQL_REQUESTS_PER_MINUTE`),
  8 running at once across the server, 20,000 characters per query, and 5 s per statement.
  Every statement is rolled back, only SELECT/WITH/INSERT/UPDATE/DELETE statements are accepted
  (no COMMIT, dynamic SQL or procedure calls), and the practice login can't change the schema.

Measured on a test run: SQL Server ~630 MB, MySQL ~200 MB, Postgres ~30 MB, API ~170 MB,
so about 1.1 GB for the whole stack. A 4 GB VM is comfortable.
