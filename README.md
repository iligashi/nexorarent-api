# Nexora Rent API (.NET)

[![CI](https://github.com/iligashi/nexorarent-api/actions/workflows/ci.yml/badge.svg)](https://github.com/iligashi/nexorarent-api/actions/workflows/ci.yml)
![.NET](https://img.shields.io/badge/.NET-10_LTS-512BD4?logo=dotnet&logoColor=white)
![C#](https://img.shields.io/badge/C%23-14-239120?logo=csharp&logoColor=white)
![MySQL](https://img.shields.io/badge/MySQL-8.4-4479A1?logo=mysql&logoColor=white)

ASP.NET Core backend for **[Nexora Rent a Car](https://github.com/iligashi/nexorarent)**, built to
replace the existing Express API **one route group at a time, without downtime**. Both backends
share the same MySQL database and the same JWT secret, and the Next.js frontend does not change:
Next.js rewrites decide which backend serves each route.

> **Status:** the public read endpoints (cars, locations, extras, blog), customer reservations,
> reviews and all of `/auth` (register, login, refresh-token rotation, logout, profile) are ported
> and tested against the Express API. The admin area has started with `/admin/reviews` and the
> audit log; the other admin groups are next.
> See the [migration plan](docs/migration-plan.md) for what comes next.

## What's here

| Endpoint | Description |
|---|---|
| `GET /api/v1/cars` | Paged list with category, fuel, transmission, price and text filters, and sorting |
| `GET /api/v1/cars/featured` | Up to 6 featured cars |
| `GET /api/v1/cars/{slug}` | Car details with image gallery and latest approved reviews |
| `GET /api/v1/cars/{id}/availability` | Whether a car is free for a period, plus its booked periods |
| `GET /api/v1/locations` | Active pickup and dropoff locations |
| `GET /api/v1/extras` | Active rental extras with daily price |
| `GET /api/v1/blog` | Published posts, newest first, paged |
| `GET /api/v1/blog/{slug}` | One published post with its author name |
| `POST /api/v1/reservations` | Book a car as a guest or signed-in customer, with extras and seasonal prices |
| `GET /api/v1/reservations/mine` | The signed-in customer's bookings |
| `GET /api/v1/reservations/{id}` | One booking with its extras (owner, or staff roles) |
| `PUT /api/v1/reservations/{id}/cancel` | Cancel a pending or confirmed booking |
| `POST /api/v1/reviews` | Review a completed booking, once |
| `POST /api/v1/auth/register` | Customer sign-up; returns `{ user, accessToken }` and sets the `refreshToken` cookie |
| `POST /api/v1/auth/login` | Sign-in with email and password (bcrypt, compatible with Express hashes) |
| `POST /api/v1/auth/refresh` | Rotates the refresh token: the old one is revoked, a new cookie and access token are issued |
| `POST /api/v1/auth/logout` | Revokes the current refresh session |
| `POST /api/v1/auth/logout-all` | Revokes every session of the signed-in user |
| `GET /api/v1/auth/me` | Current user, from an access token issued by either API |
| `PUT /api/v1/auth/me` | Update first name, last name or phone |
| `GET /api/v1/auth/me/loyalty` | Loyalty points and tier |
| `GET /api/v1/admin/reviews` | Staff: all reviews with author and car, filtered by `status` (pending, approved), paged |
| `PUT /api/v1/admin/reviews/{id}/approve`, `/reject`, `/reply` | Managers and owners: moderate and answer reviews |
| `DELETE /api/v1/admin/reviews/{id}` | Managers and owners: remove a review |
| `GET /api/health` | Database health, same shape as the Express health check |

Every endpoint keeps the Express API's URL, status codes, error messages and snake_case JSON,
so the frontend can switch per route. [Known differences](docs/migration-plan.md#known-differences-from-express)
are listed and intentional.

## Architecture

```mermaid
flowchart LR
    Browser --> Next[Next.js on Vercel]
    Next -- "/api/v1/cars/*" --> Dotnet[ASP.NET Core API]
    Next -- "everything else" --> Express[Express API]
    Dotnet --> MySQL[(MySQL)]
    Express --> MySQL
```

```
src/
  NexoraRent.Api/             Minimal API endpoints, JWT auth, CORS, rate limiting, error handling
  NexoraRent.Core/            Domain types and rules (booking periods, list queries), repository interfaces
  NexoraRent.Infrastructure/  Dapper + MySqlConnector repositories
tests/
  NexoraRent.UnitTests/         Domain rules
  NexoraRent.IntegrationTests/  Full HTTP tests against MySQL 8.4 in Docker (Testcontainers)
  NexoraRent.ParityTests/       Same requests to Express and .NET, responses must match
```

Design decisions are recorded in [`docs/adr`](docs/adr), starting with
[why Dapper instead of EF Core](docs/adr/0001-dapper-over-ef-core.md).

## Highlights

- **No double bookings across two backends:** bookings take the same MySQL `GET_LOCK` per car as
  Express and lock the car and overlapping rows inside a transaction, so Express and this API
  can both take bookings during the migration.
- **Same validation errors:** request bodies are checked with the rules and exact messages of the
  zod schemas in Express.
- **Token compatibility:** validates the HS256 access tokens Express issues (`userId` claim,
  no clock skew) and returns the same 401 messages (`Token expired`, `Invalid token`, ...).
- **Shared configuration:** reads the Express variable names (`DB_HOST`, `JWT_SECRET`,
  `CLIENT_URL`, ...), so one Railway config serves both services.
- **Contract tests:** integration tests start a real MySQL container, load the schema and seed
  data, and assert on the exact JSON the frontend reads.
- **Parity tests:** CI starts the real Express API next to this one on a seeded database and
  compares every response, so a ported route cannot drift from the original.
- **Production basics:** request ids in `X-Request-Id`, JSON logs, per-IP rate limiting,
  credentialed CORS allow-list, non-root Docker image, warnings treated as errors.

## Run locally

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download) and the MySQL database from the
nexorarent repo (`docker compose up -d` there, which listens on `127.0.0.1:3307`).

```bash
cp .env.example .env   # same DB and JWT values as the Express API
dotnet run --project src/NexoraRent.Api
```

The API listens on `http://localhost:5080`. In development, the OpenAPI document is at
`/openapi/v1.json`.

With Docker instead:

```bash
docker compose up --build
```

## Tests

```bash
dotnet test --solution NexoraRent.slnx
```

Integration tests need Docker. Without it they are skipped locally; CI sets `REQUIRE_DOCKER=true`
so they always run there.

Parity tests need both APIs running and are skipped otherwise. See
[Parity tests](docs/migration-plan.md#parity-tests) for how to run them.

## Connect the frontend

In the nexorarent repo, add rewrites to `client/next.config.ts` and set
`NEXT_PUBLIC_API_URL=/api/v1`. The full snippet is in the [migration plan](docs/migration-plan.md#how-traffic-is-split).
