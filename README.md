# CommerceOps API

CommerceOps is an ASP.NET Core REST API for inventory and order management. The first catalog release manages products and categories with EF Core and SQL Server.

## Prerequisites

- The .NET 10 SDK selected by [global.json](global.json)
- A SQL Server instance for catalog endpoints. The optional Docker example below uses SQL Server 2022 Developer Edition on Linux x64.

## Run locally

Run these commands from the repository root. If you have a local SQL Server instance, set `ConnectionStrings__Catalog` to its connection string using your shell or secret manager, then skip the container command.

The following Bash example generates a disposable local password and passes it through the environment. The container uses a named volume for local data.

```bash
export MSSQL_SA_PASSWORD="CommerceOpsAa1!$(openssl rand -hex 16)"
docker run --detach --name commerceops-sql \
  --publish 127.0.0.1:1433:1433 \
  --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD \
  --volume commerceops-sql-data:/var/opt/mssql \
  mcr.microsoft.com/mssql/server:2022-latest

export ConnectionStrings__Catalog="Server=127.0.0.1,1433;Database=CommerceOps;User ID=sa;Password=${MSSQL_SA_PASSWORD};Encrypt=True;TrustServerCertificate=True"
```

Wait for SQL Server to accept connections. For the Docker example, check `docker logs commerceops-sql` for the ready message and keep the local password for subsequent sessions. `TrustServerCertificate=True` is for the local container's self-signed certificate.

Apply the committed migration and start the API:

```sh
dotnet tool restore
dotnet ef database update --project src/CommerceOps.Infrastructure --startup-project src/CommerceOps.Api
dotnet run --project src/CommerceOps.Api -- --environment Development --urls http://localhost:5080
```

The API uses the configured database and migrations are applied explicitly. `GET /health` reports process liveness without requiring SQL Server. The OpenAPI JSON document is available in Development at `/openapi/v1.json`.

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
| DELETE | `/api/v1/products/{id}` | 204 after deletion |

Product listing is ordered by SKU and supports `categoryId`, `page` (default 1), and `pageSize` (default 20, maximum 100). IDs are server-generated GUIDs. Category and product names have a 120-character limit, descriptions are optional with a 1,000-character limit, and SKU is required with a 64-character limit. SKUs are trimmed and normalized to uppercase before storage. Product prices must be positive and fit `decimal(18,2)`. For product creation and replacement, `isActive` defaults to `true`.

Errors use Problem Details. Invalid input or an invalid product category reference returns 400, missing resources return 404, and duplicate SKUs or deletion of a category with products return 409.

### Example requests

```sh
curl --fail --silent http://localhost:5080/health
curl --fail --silent http://localhost:5080/openapi/v1.json

curl --fail --silent -X POST http://localhost:5080/api/v1/categories \
  -H 'Content-Type: application/json' \
  -d '{"name":"Hardware","description":"Parts and accessories"}'

# Replace CATEGORY_ID with the ID returned by the category request.
curl --fail --silent -X POST http://localhost:5080/api/v1/products \
  -H 'Content-Type: application/json' \
  -d '{"sku":"HW-001","name":"Steel bracket","unitPrice":5.75,"categoryId":"CATEGORY_ID","isActive":true}'

curl --fail --silent 'http://localhost:5080/api/v1/products?page=1&pageSize=20'
```

## Build and test

```sh
dotnet restore CommerceOps.slnx
dotnet build CommerceOps.slnx -c Release --no-restore
dotnet test CommerceOps.slnx -c Release --no-build
```

Catalog integration tests use relational SQLite in memory by default. To run the same suite against SQL Server, set `COMMERCEOPS_TEST_SQLSERVER` to a connection string for a **test** server. The login needs permission to create and drop databases. The suite creates an isolated database with a generated name, applies the committed migration, and drops the database after testing. GitHub Actions runs both providers with an ephemeral SQL Server container and a generated password.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `src/CommerceOps.Api` | ASP.NET Core host, HTTP endpoints, Problem Details, and OpenAPI |
| `src/CommerceOps.Application` | Catalog models, validation, and use cases independent of HTTP and EF Core |
| `src/CommerceOps.Infrastructure` | EF Core SQL Server mappings, persistence, and migrations |
| `tests/CommerceOps.Api.Tests` | xUnit unit tests and HTTP integration tests |
