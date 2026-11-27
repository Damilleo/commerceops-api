# Architecture notes

An ASP.NET Core Minimal API maps HTTP routes, validates bearer tokens, and translates application errors to Problem Details. The Application project owns catalog and inventory rules and depends on persistence interfaces. Infrastructure supplies EF Core implementations, the SQL Server model, and migrations. The API wires those implementations at startup.

## Data and transactions

- Categories and products have GUID identifiers. A product references a category, has a normalized unique SKU, and has a positive `decimal(18,2)` unit price. Deleting a category with products returns 409.
- Products start at zero stock. Receipt and issue quantities must be positive integers. A conditional SQL update compares the current balance to the permitted range before applying the delta. This prevents lost updates, negative stock, and integer overflow under concurrent requests.
- The balance update and the movement insert share a transaction. If either fails, neither commits. The movement stores a product ID, direction, quantity, UTC timestamp, and optional reference and note. History sorts by timestamp and ID descending. The API exposes only append and read operations.
- A restrictive foreign key protects movement history if a product delete races a stock write. A product with movements cannot be deleted, even if its balance returns to zero.
- Repeated inventory writes are separate movements. If you lose the response to a receipt or issue, inspect the balance and history before retrying.

## Authentication and permissions

Identity stores user accounts and roles in the same database as the catalog. Migrations add Identity tables without rebuilding the catalog or inventory tables. The CLI bootstraps the first admin and the three roles in a transaction. Admins can create users through the API.

| Operation | Anonymous | Viewer | Operator | Admin |
| --- | --- | --- | --- | --- |
| Health, catalog reads, inventory reads | Yes | Yes | Yes | Yes |
| Read own identity via `/auth/me` | | Yes | Yes | Yes |
| Receive or issue stock | | | Yes | Yes |
| Write categories or products, create users | | | | Yes |

Locally issued JWTs expire after 15 minutes. Their role is a snapshot until expiry. Login uses generic 401 errors, a per-instance limit of 20 attempts per minute per source IP, and Identity lockout for 15 minutes after five failed password attempts. Plain HTTP is only accepted for a loopback connection in Development. Use HTTPS elsewhere and configure trusted forwarded headers if TLS terminates at a proxy.

The API has no public self-registration, password reset, token refresh, or revocation. A public deployment would need a trusted OIDC/OAuth provider, centrally managed credentials and rate limits, and a review of anonymous reads. Use only fictitious data in this demo.

## Verification

xUnit unit tests cover application rules. HTTP tests exercise the application over an in-process server with ephemeral signing keys and credentials. SQLite gives fast relational feedback. SQL Server exercises the production EF provider, additive migrations, and concurrent requests with separate connections. GitHub Actions also provisions the Compose service and smoke-tests the CLI and HTTP startup flow.

To run the SQL Server test pass locally, set `COMMERCEOPS_TEST_SQLSERVER` to the connection string of a disposable test server. The suite generates separate database names, applies the migrations, and drops its own databases. The login needs permission to create and drop them. Use the commands in the [README](../README.md#build-and-test).
