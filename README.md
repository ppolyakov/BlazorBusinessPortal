# BusinessPortal

BusinessPortal is a production-style Blazor business management portal for small delivery teams. It brings clients, projects, work items, time tracking, approvals, reporting, Excel exports, and audit history into one organization-isolated workspace.

> This is an independent portfolio project. It does not represent a real client deployment or production usage claim.

## Product tour

Screenshot capture slots are prepared for the final portfolio images:

1. Dashboard — KPIs, project-hours chart, activity, and deadlines
2. Projects — client/status filters and budget utilization
3. Project delivery — work items, priorities, ownership, and due dates
4. Time approvals — manager review with approve/reject workflow
5. Reports — server aggregates, paged details, and Excel export

See [the exact screenshot routes and states](docs/screenshots/README.md).

## Live demo

No public deployment is currently configured. Run the complete demo locally with Docker Compose.

## Demo accounts

When `SeedDemoData=true`, the following Northstar Studio accounts are created:

| Role | Email |
|---|---|
| Administrator | `admin@northstar.demo` |
| Manager | `manager@northstar.demo` |
| Employee | `employee@northstar.demo` |

A second organization contains `manager@bluebird.demo` for isolation verification. Every account uses the password supplied through `DemoPassword`; no real password is committed.

## Features

- Organization-based data isolation enforced in server-side application services
- ASP.NET Core Identity with Administrator, Manager, and Employee roles
- Server-filtered and server-paged client, project, task, time, report, and audit views
- Project budget utilization based on approved time
- Draft, submit, reject, reopen, resubmit, and approve state transitions
- Self-approval prevention and PostgreSQL optimistic concurrency
- Dashboard KPIs, recent activity, deadlines, and CSS-rendered charts
- Safe `.xlsx` exports with filters, frozen headings, auto-filter, typed dates/numbers, XML sanitization, and formula neutralization
- Audit events for key changes, workflow decisions, and exports
- Responsive, keyboard-visible UI with loading, empty, success, error, 403, and 404 states

## Technology

.NET 10 LTS, ASP.NET Core 10, Blazor Web App with global Interactive Server rendering, ASP.NET Core Identity, EF Core 10, Npgsql, PostgreSQL 17, QuickGrid, Bootstrap 5, Open XML SDK, xUnit, Testcontainers, Docker Compose, and GitHub Actions.

## Architecture

The solution is a modular monolith:

- `Domain` — entities, enums, invariants, and workflow transitions
- `Application` — explicit use-case contracts, DTOs, validation models, permissions, and paging
- `Infrastructure` — PostgreSQL/EF Core, Identity persistence, services, audit, export, migration, and seeding
- `Web` — Blazor UI, authentication endpoints, routes, layout, and dependency composition

Business operations create a short-lived context through `IDbContextFactory<ApplicationDbContext>`. Read queries use `AsNoTracking`, projection, bounded paging, and explicit organization predicates. See [architecture.md](docs/architecture.md) and the [decision log](docs/decisions/).

## Entity model

```mermaid
erDiagram
    Organization ||--o{ ApplicationUser : contains
    Organization ||--o{ Client : owns
    Organization ||--o{ Project : owns
    Organization ||--o{ WorkItem : owns
    Organization ||--o{ TimeEntry : owns
    Organization ||--o{ AuditEntry : owns
    Client ||--o{ Project : has
    Project ||--o{ WorkItem : has
    Project ||--o{ TimeEntry : records
    WorkItem o|--o{ TimeEntry : categorizes
    ApplicationUser ||--o{ TimeEntry : submits
    ApplicationUser o|--o{ TimeEntry : reviews
```

## Quick start with Docker

Prerequisites: Docker Desktop or a compatible Docker Engine with Compose.

```bash
cp .env.example .env
# Replace both placeholder passwords in .env
docker compose config
docker compose build
docker compose up
```

Open `http://localhost:8080`. The one-shot `migrate` service applies migrations and idempotent demo seeding before the web service starts. PostgreSQL data and ASP.NET Core Data Protection keys use separate named volumes, so data and authentication cookies survive web-container replacement. Stop only this project with `docker compose down`; keep the volumes unless you intentionally want to reset demo data.

## Local development

Prerequisites: .NET SDK 10.0.301 or newer compatible 10.0 SDK, PostgreSQL, and Docker for integration tests.

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=business_portal;Username=business_portal;Password=<local-password>"
$env:SeedDemoData = "true"
$env:DemoPassword = "<strong-local-demo-password>"
dotnet tool restore
dotnet restore BusinessPortal.sln
dotnet run --project src/BusinessPortal.Web/BusinessPortal.Web.csproj -- --migrate --seed
dotnet run --project src/BusinessPortal.Web/BusinessPortal.Web.csproj
```

The first `dotnet run` is an explicit migration/seed operation and exits. The web process never applies production migrations implicitly.

## Migrations

Create a migration:

```bash
dotnet tool run dotnet-ef migrations add <Name> --project src/BusinessPortal.Infrastructure --startup-project src/BusinessPortal.Web --output-dir Migrations
```

Apply it explicitly:

```bash
dotnet run --project src/BusinessPortal.Web -- --migrate
```

For deployment, back up the database, run the migration job once with the target connection string, verify it, then start the new web image.

## Verification

```bash
dotnet restore BusinessPortal.sln
dotnet format BusinessPortal.sln --verify-no-changes --no-restore
dotnet build BusinessPortal.sln --configuration Release --no-restore
dotnet test BusinessPortal.sln --configuration Release --no-build
dotnet list BusinessPortal.sln package --vulnerable --include-transitive
```

Integration tests use a real disposable PostgreSQL 17 container and therefore require a running Docker daemon. The workflow in `.github/workflows/ci.yml` runs the same checks and builds the Docker image without publishing it.

## Security and isolation

Organization IDs and user IDs are obtained from the authenticated server-side identity, never trusted from forms. Every service query is tenant-scoped; manager operations are enforced in application code as well as in page authorization. Public registration is disabled. Cookies are HTTP-only, antiforgery is enabled, exports are bounded, sensitive free text is excluded from audit summaries, and production errors do not expose stack traces.

Read [SECURITY.md](SECURITY.md) for reporting guidance and `docs/risks.md` for known delivery risks.

## Technical decisions

- Modular monolith over distributed services
- Explicit application services over generic repositories and mediator layers
- Stable string enum storage for readable PostgreSQL data
- `DateOnly` for business dates and UTC for events
- `decimal(6,2)` time quantities
- PostgreSQL `xmin` optimistic concurrency for approvals
- Open XML SDK rather than a commercial spreadsheet library

## Demo limitations

- No public signup, invitation administration, password email delivery, billing, attachments, or external identity provider
- Demo seeding is intentionally opt-in and requires environment-supplied credentials
- No public hosting is included
- Performance results must be measured in the target environment; no production-scale claims are made
- Screenshots and the final portfolio video require a running seeded instance

## License

[MIT](LICENSE.txt)
