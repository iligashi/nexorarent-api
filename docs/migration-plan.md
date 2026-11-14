# Migration plan: Express → ASP.NET Core

The Next.js frontend in [nexorarent](https://github.com/iligashi/nexorarent) talks to
one base URL (`NEXT_PUBLIC_API_URL`). Routes move to this API one group at a time,
while both backends share the same MySQL database.

## How traffic is split

Next.js rewrites send each route group to the backend that owns it. The browser only
sees the frontend's own domain, so the `refreshToken` cookie stays first-party (no
third-party cookie problems in Safari) and no CORS is needed in production.

```ts
// client/next.config.ts in the nexorarent repo
const DOTNET_API = process.env.DOTNET_API_URL;   // e.g. https://nexorarent-api.up.railway.app
const EXPRESS_API = process.env.EXPRESS_API_URL; // e.g. https://nexora-rent-production.up.railway.app

async rewrites() {
  return [
    // Moved to .NET
    { source: '/api/v1/cars/:path*', destination: `${DOTNET_API}/api/v1/cars/:path*` },
    // Everything else stays on Express until it moves
    { source: '/api/v1/:path*', destination: `${EXPRESS_API}/api/v1/:path*` },
  ];
}
```

Then set `NEXT_PUBLIC_API_URL=/api/v1` on Vercel. Rolling a group back is a one-line change.

## Order

| # | Route group | Status | Notes |
|---|---|---|---|
| 1 | `GET /cars`, `/cars/featured`, `/cars/:slug`, `/cars/:id/availability` | ✅ done | Read-only, highest traffic |
| 2 | `GET /auth/me` | ✅ done | Proves Express tokens validate here |
| 3 | `GET /locations`, `/extras`, `/blog`, `/blog/:slug` | ✅ done | Read-only. `POST /reviews` is a write and moves with step 4 |
| 4 | `/reservations`, `POST /reviews` | ⏳ next | Needs the per-car lock and overlap check inside a transaction |
| 5 | `/auth/login`, `/register`, `/refresh`, `/logout` | ⏳ | Port refresh-token rotation and bcrypt verification |
| 6 | `/admin/*` | ⏳ | Role checks, audit log, image upload to object storage |
| 7 | Retire Express | ⏳ | Move schema migrations into this repo |

## Rules while both run

- **Same database, same schema.** Schema changes go through the Express migrations
  until step 7.
- **Same secrets.** `JWT_SECRET` must match so access tokens work on both sides.
- **Same JSON.** Every moved route keeps its URL, status codes, error messages and
  snake_case response shape. The integration tests pin that contract, and the parity
  tests prove it against the real Express API (see below).
- **One owner per write path.** A table's writes move together (for example all
  reservation writes in step 4) so locking logic never runs in two places.

## Parity tests

`tests/NexoraRent.ParityTests` sends the same requests to both APIs, running on one
database, and fails on any difference in status code or JSON body. The comparer only
accepts the representation differences listed below (numbers, booleans, timestamp
milliseconds); a missing key, an extra key or a changed value fails the test.

CI runs them in the `parity` job: MySQL 8.4, the Express migrations and seed, then
`Sql/fixtures.sql` for blog posts and a booking. Every new route group adds its requests
to `ParityTests.cs` before its rewrite is switched on.

To run them locally, start both APIs on the same database and set the URLs:

```bash
PARITY_EXPRESS_URL=http://localhost:4000 PARITY_DOTNET_URL=http://localhost:5080   dotnet test --project tests/NexoraRent.ParityTests
```

Express reads `DATETIME` values as UTC (mysql2 `timezone: 'Z'`). Versions before that
used the Node process time zone, so with an older Express start it with `TZ=UTC`.

## Known differences from Express

| Area | Express | This API | Why |
|---|---|---|---|
| Numbers | `DECIMAL` columns as strings (`"35.00"`) | JSON numbers (`35.0`) | Matches the client's TypeScript types; `Number()` in the client handles both |
| Booleans | `0` / `1` | `true` / `false` | Same reason |
| `limit` (cars, blog) | Unbounded | Clamped to 1–100 | Prevents very large pages |
| Search | `%` and `_` act as wildcards | Treated as literal text | Predictable search |
| 4xx messages in production | Replaced with "Internal server error" by the error handler | Real message | Client can show a useful error |
| `requestId` in error bodies | Only when the error goes through `next(err)` | On every error body, equal to the `X-Request-Id` header | Every reported error can be found in the logs. Parity tests skip this key, since each server generates its own id |
