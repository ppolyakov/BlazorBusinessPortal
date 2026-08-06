# ADR 0001: Modular monolith and vertical-slice delivery

Status: Accepted

## Context

Vela is a portfolio-scale business application with related workflows and a single operational database. It needs clear boundaries without distributed-system overhead.

## Decision

Use a modular monolith with four production projects:

- `Domain` owns entities, enums, invariants, and state transitions.
- `Application` owns use cases, result/form models, permissions, and persistence abstractions.
- `Infrastructure` owns EF Core, PostgreSQL, Identity persistence, audit, exports, and demo seeding.
- `Web` owns Blazor components, authentication endpoints, routing, and composition.

Deliver complete user scenarios vertically and keep the solution buildable after each stage. Do not add generic repositories, a redundant unit of work, MediatR, AutoMapper, or FluentValidation.

## Consequences

The design keeps business rules testable and deployment simple. Application services remain explicit; some purposeful query code is preferable to a generic abstraction that hides tenant and paging rules.
