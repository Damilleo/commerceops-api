# CommerceOps API

- Use the .NET SDK selected by `global.json` and run commands from the repository root.
- Keep HTTP routing and serialization in `src/CommerceOps.Api`. Keep application behavior in `src/CommerceOps.Application`, independent of ASP.NET Core.
- Add endpoint integration tests in `tests/CommerceOps.Api.Tests` using xUnit and `WebApplicationFactory`.
- Run `dotnet restore CommerceOps.slnx`, `dotnet build CommerceOps.slnx -c Release --no-restore`, and `dotnet test CommerceOps.slnx -c Release --no-build` before submitting changes.
