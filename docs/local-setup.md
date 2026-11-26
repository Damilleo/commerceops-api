# Local setup

Run commands from the repository root. Install the [.NET 10 SDK selected by global.json](../global.json) and Docker Compose v2. On Windows, use Docker Desktop with Linux containers on an x64 machine and PowerShell 7.2 or newer. Bash examples use OpenSSL. The first image pull needs internet access.

## 1. Configure SQL Server and JWT

Run one of the following blocks in the terminal that will run migrations and the API. On first use, it generates an ignored `.env` file. Alternatively, copy [`.env.example`](../.env.example) and fill in the password and port before running the block. Use a strong random SQL password with at least 24 random bytes encoded as hex, plus uppercase, lowercase, numeric, and punctuation characters. For this sample connection string, use the generated password format to avoid connection-string delimiters. Keep the original password for the initialized volume.

### Windows PowerShell

```powershell
if (-not (Test-Path .env)) {
    $sqlPassword = 'Aa1!' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    @("MSSQL_SA_PASSWORD=$sqlPassword", 'COMMERCEOPS_SQL_PORT=1433') | Set-Content .env -Encoding utf8NoBOM
}
$local = ConvertFrom-StringData (Get-Content -Raw .env)
if (-not $local.MSSQL_SA_PASSWORD) { throw 'Fill in MSSQL_SA_PASSWORD in .env first.' }
$sqlPort = if ($local.COMMERCEOPS_SQL_PORT) { $local.COMMERCEOPS_SQL_PORT } else { '1433' }
$env:MSSQL_SA_PASSWORD = $local.MSSQL_SA_PASSWORD
$env:COMMERCEOPS_SQL_PORT = $sqlPort
$env:ConnectionStrings__Catalog = "Server=127.0.0.1,$sqlPort;Database=CommerceOps;User ID=sa;Password=$($local.MSSQL_SA_PASSWORD);Encrypt=True;TrustServerCertificate=True"
$env:DOTNET_ENVIRONMENT = 'Development'
$env:Jwt__Issuer = 'commerceops-local'
$env:Jwt__Audience = 'commerceops-api'
$env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
Remove-Variable local, sqlPassword -ErrorAction SilentlyContinue
docker compose up -d --wait --wait-timeout 150
if ($LASTEXITCODE -ne 0) { throw 'SQL Server did not become healthy. See troubleshooting below.' }
```

### Bash

```bash
if [[ ! -f .env ]]; then
  (umask 077; printf 'MSSQL_SA_PASSWORD=Aa1!%s\nCOMMERCEOPS_SQL_PORT=1433\n' "$(openssl rand -hex 24)" > .env)
fi
export MSSQL_SA_PASSWORD="$(sed -n 's/^MSSQL_SA_PASSWORD=//p' .env | tr -d '\r')"
export COMMERCEOPS_SQL_PORT="$(sed -n 's/^COMMERCEOPS_SQL_PORT=//p' .env | tr -d '\r')"
export COMMERCEOPS_SQL_PORT="${COMMERCEOPS_SQL_PORT:-1433}"
export ConnectionStrings__Catalog="Server=127.0.0.1,$COMMERCEOPS_SQL_PORT;Database=CommerceOps;User ID=sa;Password=$MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True"
export DOTNET_ENVIRONMENT=Development
export Jwt__Issuer=commerceops-local
export Jwt__Audience=commerceops-api
export Jwt__SigningKey="$(openssl rand -base64 32)"
docker compose up -d --wait --wait-timeout 150
```

Continue only if Compose reports success. Its health check runs a SQL query with the local login. The service binds to loopback and the sample connection trusts the container's self-signed certificate for local use. Each configuration block generates a fresh JWT key so a restarted API invalidates tokens from a previous configuration block. Keep local configuration, login passwords and tokens out of commit history, screenshots and logs. Avoid printing `.env`, running plain `docker compose config`, or using shell tracing for these commands.

## 2. Apply migrations and seed the sample catalog

The same commands work in PowerShell and Bash:

```sh
dotnet tool restore
dotnet ef database update --project src/CommerceOps.Infrastructure --startup-project src/CommerceOps.Api
dotnet run --project src/CommerceOps.Api -- seed-demo
```

A clean database reports **2 categories and 3 products** added. A repeat run adds zero. The command is available only in Development and only via the CLI. It does not change existing catalog entries, add users, or create stock movements. Use a dedicated development database. Move stock using the inventory API. If a different row already uses a seed SKU, the existing row is retained.

## 3. Optionally bootstrap an admin

Anonymous reads work without an account. For the authenticated walkthrough, choose a unique password of 15 to 128 characters with uppercase, lowercase, a digit and a symbol. The CLI creates the roles and first admin in a transaction. A repeat invocation leaves the existing account intact.

### Windows PowerShell

```powershell
$env:COMMERCEOPS_ADMIN_USERNAME = 'local-admin'
$env:COMMERCEOPS_ADMIN_PASSWORD = Read-Host 'Bootstrap password' -MaskInput
try {
    dotnet run --project src/CommerceOps.Api -- bootstrap-admin
    if ($LASTEXITCODE -ne 0) { throw 'Admin bootstrap failed.' }
} finally {
    Remove-Item Env:COMMERCEOPS_ADMIN_PASSWORD
}
```

### Bash

```bash
export COMMERCEOPS_ADMIN_USERNAME=local-admin
read -rsp 'Bootstrap password: ' COMMERCEOPS_ADMIN_PASSWORD
printf '\n'
export COMMERCEOPS_ADMIN_PASSWORD
dotnet run --project src/CommerceOps.Api -- bootstrap-admin
unset COMMERCEOPS_ADMIN_PASSWORD
```

## 4. Start the app

```sh
dotnet run --project src/CommerceOps.Api -- --urls http://localhost:5080
```

From a second terminal, follow the [API examples and smoke test](api-examples.md).

## Troubleshooting and cleanup

| Symptom | What to check |
| --- | --- |
| Docker is missing or the daemon is stopped | Start Docker Desktop or install a local Compose-capable Docker engine. An existing local SQL Server also works. Set `ConnectionStrings__Catalog` to its local database and verify login and a `SELECT 1` before migrating. |
| Port 1433 is in use | Check for an existing CommerceOps or standalone SQL container with `docker ps`. Stop the old container only if appropriate, or change `COMMERCEOPS_SQL_PORT` in `.env` to a free port and rerun the configuration block. |
| SQL Server stays unhealthy or returns login errors | Check `docker compose ps` and `docker compose logs --tail 80 sql` locally. Keep the password from when the volume was first initialized. Editing `.env` alone does not change the password stored by SQL Server. |
| An older local CommerceOps container already holds the data | Point the app at the existing server and reuse its credentials. Compose creates a separate named volume. Back up valuable data before changing container or volume configuration. |
| EF reports a connection failure | Confirm Compose is healthy, the host port and database in `ConnectionStrings__Catalog` are correct, and you are running from the same configured terminal. |
| The API refuses to start or tokens stop working | Set issuer, audience, and the Base64 signing key in the same terminal as the API. Sign in again after restarting with a new key. Plain HTTP only works for local loopback Development connections. |
| The API port is busy | Choose another loopback port with `--urls` and update the request collection or example base URL. |

Stop the API with Ctrl+C. Use `docker compose down` to remove the container and retain the database volume.

**Destructive reset:** `docker compose down --volumes` deletes the Compose volume and **all local database data**, including products, inventory history, and users. Use it only for a disposable demo database after checking which Compose project is active. Then repeat the setup, migration, seed, and optional bootstrap.
