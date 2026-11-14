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
| 3 | `/locations`, `/extras`, `/blog`, `/reviews` (public reads) | ⏳ next | Read-only |
| 4 | `/reservations` | ⏳ | Needs the per-car lock and overlap check inside a transaction |
| 5 | `/auth/login`, `/register`, `/refresh`, `/logout` | ⏳ | Port refresh-token rotation and bcrypt verification |
| 6 | `/admin/*` | ⏳ | Role checks, audit log, image upload to object storage |
| 7 | Retire Express | ⏳ | Move schema migrations into this repo |

## Rules while both run

- **Same database, same schema.** Schema changes go through the Express migrations
  until step 7.
- **Same secrets.** `JWT_SECRET` must match so access tokens work on both sides.
- **Same JSON.** Every moved route keeps its URL, status codes, error messages and
  snake_case response shape. The integration tests pin that contract.
- **One owner per write path.** A table's writes move together (for example all
  reservation writes in step 4) so locking logic never runs in two places.

## Known differences from Express

| Area | Express | This API | Why |
|---|---|---|---|
| Numbers | `DECIMAL` columns as strings (`"35.00"`) | JSON numbers (`35.0`) | Matches the client's TypeScript types; `Number()` in the client handles both |
| Booleans | `0` / `1` | `true` / `false` | Same reason |
| `limit` | Unbounded | Clamped to 1–100 | Prevents very large pages |
| Search | `%` and `_` act as wildcards | Treated as literal text | Predictable search |
| 4xx messages in production | Replaced with "Internal server error" by the error handler | Real message | Client can show a useful error |
