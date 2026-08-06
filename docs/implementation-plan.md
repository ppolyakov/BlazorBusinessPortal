# Vela Implementation Plan

This plan tracks delivery as tested vertical slices.

## Stage 0 — Discovery and planning

- Inspect the repository, active instructions, SDK, Docker, and Git.
- Record architectural decisions, assumptions, and delivery risks.
- Define verification gates for every subsequent stage.

## Stage 1 — Solution foundation

- Create the modular monolith solution and project references.
- Configure `net10.0`, nullable reference types, analyzers, and formatting.
- Create the Blazor Web App shell with global Interactive Server rendering.
- Replace template content with the Vela navigation and visual system.

## Stage 2 — PostgreSQL, Identity, and organizations

- Add EF Core, Npgsql, ASP.NET Core Identity, and a design-time context factory.
- Model organizations and users, configure roles, disable public registration.
- Add migrations and opt-in, idempotent demo data seeding.
- Resolve the current user and organization on the server.

## Stage 3 — Clients

- Implement client domain data, persistence, authorization, filtering, sorting, and paging.
- Add create/edit UI and audit events.
- Add domain, application, and PostgreSQL integration coverage.

## Stage 4 — Projects and work items

- Implement project and work-item business rules, indexes, services, and UI.
- Add project details, budget utilization, filters, and status changes.
- Verify tenant isolation and domain validation.

## Stage 5 — Time tracking

- Implement draft creation, editing, deletion, filtering, totals, and submission.
- Enforce ownership and immutable submitted entries.
- Test allowed and forbidden state transitions.

## Stage 6 — Approval workflow

- Implement approval queue, approve/reject/resubmit, self-approval prevention, and audit.
- Use optimistic concurrency so competing reviews cannot both succeed.
- Test race and workflow paths.

## Stage 7 — Dashboard and reports

- Add server-side KPI, activity, deadline, aggregate, and report queries.
- Add bounded server paging and a reproducible report-query smoke test.
- Inspect the generated SQL for critical queries.

## Stage 8 — Excel export and audit UI

- Generate safe `.xlsx` reports with Open XML SDK and bounded exports.
- Add administrator-only audit search.
- Validate workbook structure and sensitive-data exclusions.

## Stage 9 — UI polish

- Complete responsive, accessible loading, empty, success, error, 403, and 404 states.
- Verify keyboard focus, labels, validation associations, and responsive layouts.

## Stage 10 — Docker and CI

- Add the non-root multi-stage image, PostgreSQL Compose stack, health checks, and explicit migrations.
- Add GitHub Actions parity for formatting, build, tests, package audit, and image build.
- Verify the full Compose workflow.

## Stage 11 — Documentation and portfolio

- Complete README, contribution/security guidance, architecture and ER diagrams.
- Add Upwork copy, screenshot plan, and 60–90 second video script.

## Stage 12 — Final revision

- Run restore, format verification, Release build, all tests, package vulnerability audit, migrations, and Docker checks.
- Review the complete diff, secrets, dependencies, and documented limitations.
- Exercise the primary scenario for each role and verify two-organization isolation.
