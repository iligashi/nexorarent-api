# 1. Dapper and hand-written SQL instead of EF Core

**Status:** accepted · November 2026

## Context

This API replaces the Express API of [nexorarent](https://github.com/iligashi/nexorarent)
route by route. During the migration both services read and write the **same MySQL
database**, and the schema is owned by the SQL migrations in the Express repository.

EF Core would normally be the default for a .NET API, but here:

- The schema already exists and must not change under the Express API. EF Core
  migrations would compete with the existing migration files.
- The best MySQL provider, Pomelo, has no release for EF Core 10. Oracle's provider
  works but is less widely used and has a weaker track record.
- The first routes to move are read-heavy and their SQL already exists in Express.
  Porting that SQL keeps the two implementations easy to compare.

## Decision

Use **Dapper** with **MySqlConnector** and hand-written SQL in repository classes
under `NexoraRent.Infrastructure`. Endpoints depend only on the repository interfaces
in `NexoraRent.Core`.

## Consequences

- Responses can be compared query-for-query with the Express routes.
- SQL is checked by integration tests against a real MySQL 8.4 container, not by a compiler.
- Schema changes still go through the Express migrations until the Express API is retired.
  At that point, moving migrations into this repository (and reconsidering EF Core) is a
  separate decision.
