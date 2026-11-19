# CommerceOps API

CommerceOps is an ASP.NET Core REST API for inventory and order management. The Sprint 0 bootstrap provides a liveness endpoint, an OpenAPI document, and integration tests.

## Prerequisites

Install the .NET 10 SDK selected by [global.json](global.json).

## Build and test

Run these commands from the repository root:

```sh
dotnet restore CommerceOps.slnx
dotnet build CommerceOps.slnx -c Release --no-restore
dotnet test CommerceOps.slnx -c Release --no-build
```

GitHub Actions runs these same commands on pushes and pull requests.

## Run locally

```sh
dotnet run --project src/CommerceOps.Api -- --environment Development --urls http://localhost:5080
```

```sh
curl --fail --silent http://localhost:5080/health
curl --fail --silent http://localhost:5080/openapi/v1.json
```

`GET /health` returns HTTP 200 with `{"status":"healthy"}` when the API is running. The endpoint checks process liveness. The OpenAPI JSON document is available in the Development environment.

## Solution layout

| Project | Responsibility |
| --- | --- |
| `src/CommerceOps.Api` | ASP.NET Core host, HTTP endpoints, and OpenAPI |
| `src/CommerceOps.Application` | Application behavior independent of HTTP |
| `tests/CommerceOps.Api.Tests` | xUnit integration tests for the HTTP contract |
