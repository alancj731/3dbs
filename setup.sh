#!/usr/bin/env bash
# Local development setup (for a server, see deploy/README.md): start the 3 databases and restore AdventureWorksLT into SQL Server.
# MySQL and Postgres are populated from SQL Server by the API on its first start.
set -euo pipefail
cd "$(dirname "$0")"

BAK=db/mssql/AdventureWorksLT2022.bak
SA_PASSWORD='AwPractice!2026'
PRACTICE_PASSWORD='Practice!2026'   # must match PracticePassword in api/appsettings.json

if [[ ! -f $BAK ]]; then
  echo "Downloading AdventureWorksLT2022.bak..."
  curl -fSL -o "$BAK" https://github.com/Microsoft/sql-server-samples/releases/download/adventureworks/AdventureWorksLT2022.bak
fi

echo "Starting containers..."
docker compose up -d --wait

sqlcmd() { docker exec -i aw-mssql /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD" -b "$@"; }

if [[ "$(sqlcmd -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name='AdventureWorksLT'")" == "0" ]]; then
  echo "Restoring AdventureWorksLT into SQL Server..."
  docker cp "$BAK" aw-mssql:/tmp/aw.bak
  sqlcmd -Q "RESTORE DATABASE AdventureWorksLT FROM DISK='/tmp/aw.bak'
    WITH MOVE 'AdventureWorksLT2022_Data' TO '/var/opt/mssql/data/AdventureWorksLT.mdf',
         MOVE 'AdventureWorksLT2022_Log'  TO '/var/opt/mssql/data/AdventureWorksLT_log.ldf', REPLACE"
fi

sqlcmd -v PRACTICE_PASSWORD="$PRACTICE_PASSWORD" < db/mssql/practice-login.sql
echo "Done. Next: dotnet run --project api   (seeds MySQL + Postgres on first start)"
