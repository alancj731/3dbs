#!/usr/bin/env bash
# Deploy or update the SQL Practice backend on this VM.
#
#   ./deploy.sh          first deploy, or redeploy after code changes (safe to re-run)
#   ./deploy.sh update   git pull, then deploy
#   ./deploy.sh status   container status and memory use
#   ./deploy.sh logs     follow API + Caddy logs
#
set -euo pipefail
cd "$(dirname "$0")"

compose() { docker compose --env-file .env "$@"; }

case "${1:-deploy}" in
  status) compose ps; docker stats --no-stream --format 'table {{.Name}}\t{{.MemUsage}}\t{{.CPUPerc}}'; exit ;;
  logs)   compose logs -f --tail=100 api caddy; exit ;;
  update) git -C .. pull --ff-only ;;
  deploy) ;;
  *) echo "usage: $0 [deploy|update|status|logs]"; exit 1 ;;
esac

# ---- Check settings ----------------------------------------------------------
[[ -f .env ]] || { echo "Missing deploy/.env. Run: cp .env.example .env, then edit it."; exit 1; }
set -a; source .env; set +a

check_password() {
  local name=$1 value=${!1:-}
  [[ -n $value ]] || { echo "$name is empty in .env"; exit 1; }
  [[ $value =~ ^[A-Za-z0-9!@#%^*_+=.-]+$ ]] || { echo "$name may only use letters, digits and ! @ # % ^ * - _ + = ."; exit 1; }
  local classes=0
  [[ $value =~ [a-z] ]] && ((classes++)) || true
  [[ $value =~ [A-Z] ]] && ((classes++)) || true
  [[ $value =~ [0-9] ]] && ((classes++)) || true
  [[ $value =~ [^A-Za-z0-9] ]] && ((classes++)) || true
  (( ${#value} >= 12 && classes >= 3 )) || { echo "$name needs 12+ characters using 3 of: upper, lower, digit, symbol"; exit 1; }
}
check_password DB_ADMIN_PASSWORD
check_password PRACTICE_PASSWORD
[[ ${API_DOMAIN:-api.example.com} != api.example.com ]] || { echo "Set API_DOMAIN in .env"; exit 1; }

# ---- Databases -----------------------------------------------------------------
BAK=../db/mssql/AdventureWorksLT2022.bak
if [[ ! -f $BAK ]]; then
  echo "==> Downloading AdventureWorksLT2022.bak"
  curl -fSL -o "$BAK" https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorksLT2022.bak
fi

echo "==> Starting databases"
compose up -d --wait mssql mysql postgres

sqlcmd() { compose exec -T -e SQLCMDPASSWORD="$DB_ADMIN_PASSWORD" mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b "$@"; }

if [[ "$(sqlcmd -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name='AdventureWorksLT'" | tr -d '[:space:]')" == "0" ]]; then
  echo "==> Restoring AdventureWorksLT into SQL Server"
  compose cp "$BAK" mssql:/tmp/aw.bak
  sqlcmd -Q "RESTORE DATABASE AdventureWorksLT FROM DISK='/tmp/aw.bak'
    WITH MOVE 'AdventureWorksLT2022_Data' TO '/var/opt/mssql/data/AdventureWorksLT.mdf',
         MOVE 'AdventureWorksLT2022_Log'  TO '/var/opt/mssql/data/AdventureWorksLT_log.ldf', REPLACE"
fi

echo "==> Creating the practice login"
sqlcmd -v PRACTICE_PASSWORD="$PRACTICE_PASSWORD" < ../db/mssql/practice-login.sql > /dev/null

# ---- API + HTTPS -----------------------------------------------------------------
echo "==> Building and starting the API and Caddy"
compose up -d --build api caddy

echo "==> Waiting for the API (first start copies the data into MySQL and Postgres)"
for _ in $(seq 1 60); do
  if compose exec -T caddy wget -qO- http://api:8080/api/exercises > /dev/null 2>&1; then
    echo "API is up. Open https://$API_DOMAIN/api/exercises to check HTTPS."
    docker image prune -f > /dev/null
    exit 0
  fi
  sleep 3
done
echo "API did not come up. Recent logs:"; compose logs --tail=50 api; exit 1
