# CommerceOps API

CommerceOps is an ASP.NET Core REST API for inventory and order management. It manages products, categories, stock balances, and movement history with EF Core and SQL Server, and uses ASP.NET Core Identity and JWT bearer authentication for writes.

## Prerequisites

- The .NET 10 SDK selected by [global.json](global.json)
- A SQL Server instance for catalog and inventory endpoints. The optional Docker example below uses SQL Server 2022 Developer Edition on Linux x64.

## Run locally

Use only sample, non-sensitive data in the portfolio demo. Catalog and inventory reads are public.

Run these commands from the repository root. If you have a local SQL Server instance, set `ConnectionStrings__Catalog` to its connection string using your shell or secret manager, then skip the container command.

The container commands generate a disposable local password and pass it through the environment. Use the section for your shell. The named volume preserves local data.

### Bash

```bash
export MSSQL_SA_PASSWORD="CommerceOpsAa1!$(openssl rand -hex 16)"
docker run --detach --name commerceops-sql \
  --publish 127.0.0.1:1433:1433 \
  --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD \
  --volume commerceops-sql-data:/var/opt/mssql \
  mcr.microsoft.com/mssql/server:2022-latest

export ConnectionStrings__Catalog="Server=127.0.0.1,1433;Database=CommerceOps;User ID=sa;Password=${MSSQL_SA_PASSWORD};Encrypt=True;TrustServerCertificate=True"
```

### PowerShell 7

```powershell
$env:MSSQL_SA_PASSWORD = 'CommerceOpsAa1!' + [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16))
docker run --detach --name commerceops-sql --publish 127.0.0.1:1433:1433 --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD --volume commerceops-sql-data:/var/opt/mssql mcr.microsoft.com/mssql/server:2022-latest

$env:ConnectionStrings__Catalog = "Server=127.0.0.1,1433;Database=CommerceOps;User ID=sa;Password=$($env:MSSQL_SA_PASSWORD);Encrypt=True;TrustServerCertificate=True"
```

### Configure JWT, migrate, and create the first admin

Wait for SQL Server to accept connections. For the Docker example, check `docker logs commerceops-sql` for the ready message and keep the local password for subsequent sessions. `TrustServerCertificate=True` is for the local container's self-signed certificate.

Set a local issuer, audience, and a Base64-encoded signing key generated from at least 32 random bytes. These values can also be set through an app secret store under `Jwt:Issuer`, `Jwt:Audience`, and `Jwt:SigningKey`. Keep the same key for as long as tokens must remain valid.

Bash

```bash
export Jwt__Issuer='commerceops-local'
export Jwt__Audience='commerceops-api'
export Jwt__SigningKey="$(openssl rand -base64 32)"
```

PowerShell 7.2+

```powershell
$env:Jwt__Issuer = 'commerceops-local'
$env:Jwt__Audience = 'commerceops-api'
$env:Jwt__SigningKey = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Apply the committed migrations from either shell:

```sh
dotnet tool restore
dotnet ef --version
dotnet ef database update --project src/CommerceOps.Infrastructure --startup-project src/CommerceOps.Api
```

Run the one-time admin bootstrap locally. Use a unique password of 15 to 128 characters with uppercase, lowercase, a digit, and a symbol. The CLI creates the roles and first administrator in one transaction. After it succeeds, rerunning it leaves the account unchanged.

Bash

```bash
export COMMERCEOPS_ADMIN_USERNAME='local-admin'
read -rsp 'Bootstrap password: ' COMMERCEOPS_ADMIN_PASSWORD
printf '\n'
export COMMERCEOPS_ADMIN_PASSWORD
dotnet run --project src/CommerceOps.Api -- bootstrap-admin
unset COMMERCEOPS_ADMIN_PASSWORD
```

PowerShell 7.2+

```powershell
$env:COMMERCEOPS_ADMIN_USERNAME = 'local-admin'
$env:COMMERCEOPS_ADMIN_PASSWORD = Read-Host 'Bootstrap password' -MaskInput
try {
    dotnet run --project src/CommerceOps.Api -- bootstrap-admin
    if ($LASTEXITCODE -ne 0) { throw 'Administrator bootstrap failed.' }
} finally {
    Remove-Item Env:COMMERCEOPS_ADMIN_PASSWORD
}
```

Keep the generated JWT configuration in the shell and start the app:

```sh
dotnet run --project src/CommerceOps.Api -- --environment Development --urls http://localhost:5080
```

The API uses the configured database and migrations are applied explicitly. `GET /health` reports process liveness without requiring SQL Server. The OpenAPI JSON document is available in Development at `/openapi/v1.json`.

## Authentication and permissions

`POST /api/v1/auth/login` accepts `userName` and `password`. It returns `accessToken`, `tokenType` (`Bearer`), and `expiresIn` (900 seconds). Send the token in the `Authorization: Bearer <token>` header. `GET /api/v1/auth/me` returns the authenticated user's ID, username, and token roles.

`POST /api/v1/users` accepts `userName`, `password`, and one role (`Admin`, `Operator`, or `Viewer`). It returns HTTP 201 with the new account's ID, username, and role. Invalid input returns 400 and duplicate usernames return 409.

| Operation | Anonymous | Viewer | Operator | Admin |
| --- | --- | --- | --- | --- |
| Health, catalog reads, inventory reads | Yes | Yes | Yes | Yes |
| Read own identity via `/auth/me` | | Yes | Yes | Yes |
| Receive or issue stock | | | Yes | Yes |
| Write categories or products, create users | | | | Yes |

Protected routes return 401 for missing, expired, or invalid tokens and 403 for insufficient roles. Login failures share a generic 401 response. Each API instance allows 20 login attempts per minute per source IP. Identity locks an account for 15 minutes after five failed password attempts. The API exposes neither self-registration nor password reset.

### Login and authenticated requests

Bash examples use `jq`. Run these from a second terminal after starting the API, using the username and password selected during bootstrap.

```bash
api='http://localhost:5080'
export COMMERCEOPS_ADMIN_USERNAME='local-admin'
read -rsp 'Login password: ' COMMERCEOPS_ADMIN_PASSWORD
printf '\n'
export COMMERCEOPS_ADMIN_PASSWORD
token=$(jq -nc '{userName:env.COMMERCEOPS_ADMIN_USERNAME,password:env.COMMERCEOPS_ADMIN_PASSWORD}' |
  curl --fail --silent --show-error -H 'Content-Type: application/json' --data-binary @- "$api/api/v1/auth/login" |
  jq -er '.accessToken')
unset COMMERCEOPS_ADMIN_PASSWORD
curl --fail --silent --show-error -H "Authorization: Bearer $token" "$api/api/v1/auth/me"
```

PowerShell 7.2+

```powershell
$api = 'http://localhost:5080'
$password = Read-Host 'Login password' -MaskInput
try {
    $body = @{ userName = 'local-admin'; password = $password } | ConvertTo-Json -Compress
    $login = Invoke-RestMethod -Method Post -Uri "$api/api/v1/auth/login" -ContentType 'application/json' -Body $body
} finally {
    Remove-Variable password, body -ErrorAction SilentlyContinue
}
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod -Uri "$api/api/v1/auth/me" -Headers $headers
Invoke-RestMethod -Method Post -Uri "$api/api/v1/categories" -Headers $headers -ContentType 'application/json' -Body '{"name":"Hardware"}'
```

### Security boundaries

Identity uses the standard `AspNetUsers`, `AspNetRoles`, and related tables in the same database and migration chain as the catalog. Passwords are validated and hashed by ASP.NET Core Identity. Store signing keys and database credentials in environment secrets or a secret manager. Keep request bodies, tokens, and authorization headers out of logs, including proxy logs and shell tracing.

Plain HTTP is accepted only for a loopback connection in Development. Use HTTPS elsewhere. If TLS terminates at a reverse proxy, configure trusted forwarded headers before the HTTPS check and avoid trusting client-supplied proxy headers.

Local JWT issuance is a demo sign-in flow. Tokens are signed with HS256 and roles are taken from the signed token until it expires. There are no refresh tokens or token revocation. For a public deployment, replace local token issuance with a trusted OIDC/OAuth provider such as Microsoft Entra ID, review the anonymous read policy, and use centrally managed credentials and rate limits.

## Catalog endpoints

| Method | Path | Success |
| --- | --- | --- |
| GET | `/api/v1/categories` | 200 with categories ordered by name |
| GET | `/api/v1/categories/{id}` | 200 with the category |
| POST | `/api/v1/categories` | 201 with the category and its `Location` |
| PUT | `/api/v1/categories/{id}` | 204 after replacing category fields |
| DELETE | `/api/v1/categories/{id}` | 204 when the category has no products |
| GET | `/api/v1/products` | 200 with `items`, `page`, `pageSize`, and `totalCount` |
| GET | `/api/v1/products/{id}` | 200 with the product |
| POST | `/api/v1/products` | 201 with the product and its `Location` |
| PUT | `/api/v1/products/{id}` | 204 after replacing product fields |
| DELETE | `/api/v1/products/{id}` | 204 when the product has no stock movements |

Product listing is ordered by SKU and supports `categoryId`, `page` (default 1), and `pageSize` (default 20, maximum 100). IDs are server-generated GUIDs. Category and product names have a 120-character limit, descriptions are optional with a 1,000-character limit, and SKU is required with a 64-character limit. SKUs are trimmed and normalized to uppercase before storage. Product prices must be positive and fit `decimal(18,2)`. For product creation and replacement, `isActive` defaults to `true`.

Errors use Problem Details. Invalid input or an invalid product category reference returns 400, missing resources return 404, and duplicate SKUs or deletion of a category with products or a product with movements return 409.

### Example requests

```sh
curl --fail --silent http://localhost:5080/health
curl --fail --silent http://localhost:5080/openapi/v1.json

curl --fail --silent -X POST http://localhost:5080/api/v1/categories -H "Authorization: Bearer $token" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Hardware","description":"Parts and accessories"}'

# Replace CATEGORY_ID with the ID returned by the category request.
curl --fail --silent -X POST http://localhost:5080/api/v1/products -H "Authorization: Bearer $token" \
  -H 'Content-Type: application/json' \
  -d '{"sku":"HW-001","name":"Steel bracket","unitPrice":5.75,"categoryId":"CATEGORY_ID","isActive":true}'

curl --fail --silent 'http://localhost:5080/api/v1/products?page=1&pageSize=20'
```

## Inventory endpoints

Inventory routes have the prefix `/api/v1/inventory/{productId}`. Use the product ID returned by the catalog API.

| Method | Path after prefix | Success |
| --- | --- | --- |
| GET | *(none)* | 200 with `productId` and `stockQuantity` |
| POST | `/receipts` | 200 with the updated `balance` and recorded `movement` |
| POST | `/issues` | 200 with the updated `balance` and recorded `movement` |
| GET | `/movements` | 200 with `items`, `page`, `pageSize`, and `totalCount` |

Products start with zero stock. A receipt or issue accepts a positive integer `quantity` and optional `reference` (100 characters) and `note` (500 characters). Movement types are `receipt` and `issue`. Each movement has its own ID and a UTC timestamp. History defaults to page 1 and 20 items per page (maximum 100), ordered by timestamp descending and then ID descending. The API exposes no movement edit or delete route.

Problem Details use 400 for invalid input, 404 for missing products, and 409 for insufficient stock, receipt overflow, or database concurrency conflicts. The maximum balance is 2,147,483,647 units. Identical write requests are separate movements, so check the balance and history before resubmitting a request whose outcome is unknown.

### Inventory examples

```bash
# Replace PRODUCT_ID with the ID returned by the product creation request.
inventory='http://localhost:5080/api/v1/inventory/PRODUCT_ID'
curl --fail --silent "$inventory"
curl --fail --silent -X POST "$inventory/receipts" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  -d '{"quantity":5,"reference":"PO-42"}'
curl --fail --silent -X POST "$inventory/issues" -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  -d '{"quantity":2,"note":"Manual issue"}'
curl --fail --silent "$inventory/movements?page=1&pageSize=20"
```

### Stock consistency

A single conditional SQL update changes the balance only if the result would fit within the allowed range. The database serializes competing writes to the same product row and evaluates each update against the current balance. The update and the movement insert are in one transaction, so a failed insert rolls back the balance change. A restrictive foreign key protects movement history if a product delete races a stock write.

## Build and test

```sh
dotnet restore CommerceOps.slnx
dotnet build CommerceOps.slnx -c Release --no-restore
dotnet test CommerceOps.slnx -c Release --no-build
```

Integration tests use relational SQLite in memory by default. Tests generate fresh signing keys and passwords in the test process. SQLite verifies endpoint behavior, constraints, and rollback. The simultaneous-issue and migration upgrade tests run only against SQL Server. The migration test verifies that adding Identity preserves a Sprint 2 product, stock balance, and movement. The concurrency test uses separate connections. To run the same suite against SQL Server, set `COMMERCEOPS_TEST_SQLSERVER` to a connection string for a **test** server. The login needs permission to create and drop databases. The suite creates an isolated database with a generated name, applies the committed migrations, and drops the database after testing. GitHub Actions runs both providers with an ephemeral SQL Server container and a generated password.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `src/CommerceOps.Api` | ASP.NET Core host, HTTP endpoints, Problem Details, and OpenAPI |
| `src/CommerceOps.Application` | Catalog and inventory models, validation, and use cases independent of HTTP and EF Core |
| `src/CommerceOps.Infrastructure` | EF Core SQL Server mappings, persistence, and migrations |
| `tests/CommerceOps.Api.Tests` | xUnit unit tests and HTTP integration tests |
