# ADR 0002: PostgreSQL persistence and explicit tenant scoping

Status: Accepted

## Decision

- Use PostgreSQL through Npgsql and EF Core 10.
- Store enums as stable strings and cover conversion behavior in tests.
- Use `DateOnly` for business dates, UTC timestamps for events, and `decimal(6,2)` for hours.
- Resolve the organization from the authenticated server-side user.
- Assign organization IDs inside application services and scope every business query explicitly.
- Add organization-scoped unique indexes, including project code.
- Use `IDbContextFactory<ApplicationDbContext>` and a short-lived context per operation.
- Use optimistic concurrency for approval decisions.

## Consequences

Tenant boundaries remain visible in each use case and are testable against real PostgreSQL. Forms never accept a trusted organization ID.

