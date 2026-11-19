# CommerceOps API

CommerceOps is an ASP.NET Core REST API for inventory and order management. It manages products, categories, stock balances, and movement history with EF Core and SQL Server.

## Prerequisites

- The .NET 10 SDK selected by [global.json](global.json)
- A SQL Server instance for catalog and inventory endpoints. The optional Docker example below uses SQL Server 2022 Developer Edition on Linux x64.

## Run locally

Catalog and inventory writes are unauthenticated. Do not expose this API publicly with real data until authentication and authorization are in place.

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

### Migrate and start the API

Wait for SQL Server to accept connections. For the Docker example, check `docker logs commerceops-sql` for the ready message and keep the local password for subsequent sessions. `TrustServerCertificate=True` is for the local container's self-signed certificate.

Apply the committed migration and start the API:

```sh
dotnet tool restore
dotnet ef --version
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
| DELETE | `/api/v1/products/{id}` | 204 when the product has no stock movements |

Product listing is ordered by SKU and supports `categoryId`, `page` (default 1), and `pageSize` (default 20, maximum 100). IDs are server-generated GUIDs. Category and product names have a 120-character limit, descriptions are optional with a 1,000-character limit, and SKU is required with a 64-character limit. SKUs are trimmed and normalized to uppercase before storage. Product prices must be positive and fit `decimal(18,2)`. For product creation and replacement, `isActive` defaults to `true`.

Errors use Problem Details. Invalid input or an invalid product category reference returns 400, missing resources return 404, and duplicate SKUs or deletion of a category with products or a product with movements return 409.

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
curl --fail --silent -X POST "$inventory/receipts" -H 'Content-Type: application/json' \
  -d '{"quantity":5,"reference":"PO-42"}'
curl --fail --silent -X POST "$inventory/issues" -H 'Content-Type: application/json' \
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

Catalog and inventory integration tests use relational SQLite in memory by default. SQLite verifies endpoint behavior, constraints, and rollback. The simultaneous-issue and existing-product migration tests run only against SQL Server. The concurrency test uses separate connections. To run the same suite against SQL Server, set `COMMERCEOPS_TEST_SQLSERVER` to a connection string for a **test** server. The login needs permission to create and drop databases. The suite creates an isolated database with a generated name, applies the committed migrations, and drops the database after testing. GitHub Actions runs both providers with an ephemeral SQL Server container and a generated password.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `src/CommerceOps.Api` | ASP.NET Core host, HTTP endpoints, Problem Details, and OpenAPI |
| `src/CommerceOps.Application` | Catalog and inventory models, validation, and use cases independent of HTTP and EF Core |
| `src/CommerceOps.Infrastructure` | EF Core SQL Server mappings, persistence, and migrations |
| `tests/CommerceOps.Api.Tests` | xUnit unit tests and HTTP integration tests |
