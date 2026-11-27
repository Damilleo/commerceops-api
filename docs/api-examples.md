# API walkthrough

Start with a clean local database using the [setup guide](local-setup.md). Run `seed-demo`, bootstrap an admin for the write examples, and start the API.

## Repeatable smoke test

1. Open `http://localhost:5080/health`. Expect 200 and `{"status":"healthy"}`.
2. Open `http://localhost:5080/api/v1/products`. Expect three products from the seed. No token is required.
3. Request `GET /api/v1/inventory/c01d0000-0000-4000-8000-000000000101`. A clean `DEMO-BOX-12` product starts at zero.
4. Try an inventory receipt without a token. Expect 401. After admin sign-in, receive five units and issue two on a fresh product. Each write returns 200 and the new balance. The final balance is three and history lists the issue before the receipt.
5. Try issuing more stock than is available on that product. Expect 409 with Problem Details. The balance and history stay unchanged.

## HTTP collection

Open [`requests/commerceops.http`](../requests/commerceops.http) with the **REST Client** extension for VS Code. The file uses its named-request and process-environment syntax. The requests are also readable as plain HTTP examples and can be copied into another client.

Supply `COMMERCEOPS_DEMO_ADMIN_PASSWORD` and `COMMERCEOPS_DEMO_VIEWER_PASSWORD` to the editor process from a trusted local environment. Enter the admin password selected at bootstrap. Generate a separate password for the fictitious Viewer account, following the same password rules. For raw HTTP examples, use passwords without JSON quote or backslash characters, such as uppercase, lowercase, digit, and punctuation characters followed by randomly generated hex. Set the values before starting the editor and avoid request history or logs that retain credentials or tokens. Change `adminUser` and `baseUrl` in the collection for your local setup.

Run requests in order if using named responses. Category, product, and user creation are intentionally stateful. The scripted write walkthrough assumes a clean database. For another run, change the SKU and Viewer name or use the [disposable database reset](local-setup.md#troubleshooting-and-cleanup). The collection includes anonymous 401, Viewer 403, and insufficient-stock 409 examples.

## Windows PowerShell alternative

```powershell
$api = 'http://localhost:5080'
Invoke-RestMethod "$api/health"
Invoke-RestMethod "$api/api/v1/products"
$password = Read-Host 'Admin password' -MaskInput
try {
    $body = @{ userName = 'local-admin'; password = $password } | ConvertTo-Json -Compress
    $login = Invoke-RestMethod -Method Post -Uri "$api/api/v1/auth/login" -ContentType 'application/json' -Body $body
} finally {
    Remove-Variable password, body -ErrorAction SilentlyContinue
}
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Invoke-RestMethod -Uri "$api/api/v1/auth/me" -Headers $headers
Invoke-RestMethod -Method Post -Uri "$api/api/v1/categories" -Headers $headers -ContentType 'application/json' -Body '{"name":"Packing station"}'
```

## Bash alternative

This example uses `jq` to construct JSON safely without echoing the password or token.

```bash
api='http://localhost:5080'
curl -fsS "$api/health"
curl -fsS "$api/api/v1/products"
export DEMO_LOGIN_NAME=local-admin
read -rsp 'Admin password: ' DEMO_LOGIN_PASSWORD
printf '\n'
export DEMO_LOGIN_PASSWORD
token=$(jq -nc '{userName:env.DEMO_LOGIN_NAME,password:env.DEMO_LOGIN_PASSWORD}' |
  curl -fsS -H 'Content-Type: application/json' --data-binary @- "$api/api/v1/auth/login" |
  jq -er '.accessToken')
unset DEMO_LOGIN_PASSWORD
curl -fsS -H "Authorization: Bearer $token" "$api/api/v1/auth/me"
curl -fsS -H "Authorization: Bearer $token" -H 'Content-Type: application/json' \
  -d '{"name":"Packing station"}' "$api/api/v1/categories"
```

## Endpoint reference

The Development [OpenAPI document](http://localhost:5080/openapi/v1.json) describes request and response schemas.

| Route | Behavior |
| --- | --- |
| `GET /health` | 200 process liveness. This does not probe SQL Server. |
| `GET /api/v1/categories[/{id}]` | 200 list or individual category. |
| `GET /api/v1/products[/{id}]` | 200 paginated SKU-ordered list or individual product. The list supports `categoryId`, `page` and `pageSize`. |
| `POST /api/v1/categories`, `POST /api/v1/products` | Admin. 201 with the resource and its `Location`. Duplicate SKU returns 409. |
| `PUT` or `DELETE` on category and product IDs | Admin. 204 on success. Related products or movements can block deletion with 409. |
| `GET /api/v1/inventory/{productId}` | 200 with the current quantity. |
| `GET /api/v1/inventory/{productId}/movements` | 200 with newest-first paginated movement history. |
| `POST /api/v1/inventory/{productId}/receipts` or `/issues` | Admin or Operator. 200 with balance and movement. Insufficient stock or overflow returns 409. |
| `POST /api/v1/auth/login` | 200 with a 900-second bearer token, 401 for invalid credentials, 429 if rate-limited. |
| `GET /api/v1/auth/me` | Authenticated. 200 with the current ID, username and role. |
| `POST /api/v1/users` | Admin. 201 with ID, username and role. Duplicate username returns 409. |

Product and movement pagination defaults to page 1 and 20 items per page, with a maximum page size of 100. Invalid requests return 400, missing resources return 404, missing or invalid authentication returns 401, and insufficient permissions return 403.
