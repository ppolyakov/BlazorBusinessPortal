# Contributing

1. Install the .NET 10 SDK and Docker.
2. Restore local tools and packages with `dotnet tool restore` and `dotnet restore BusinessPortal.sln`.
3. Keep organization scoping and server-side authorization explicit in every use case.
4. Add or update tests for business rules and PostgreSQL behavior.
5. Before submitting changes, run:

```bash
dotnet format BusinessPortal.sln
dotnet build BusinessPortal.sln --configuration Release
dotnet test BusinessPortal.sln --configuration Release --no-build
dotnet list BusinessPortal.sln package --vulnerable --include-transitive
docker compose config
```

Do not commit `.env`, connection strings, passwords, tokens, generated exports, or employer/client code and data.
