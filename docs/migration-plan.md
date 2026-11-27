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
| 4 | `/reservations` (create, mine, get, cancel), `POST /reviews` | ✅ done | Same per-car `GET_LOCK` and row locks as Express, so both can take bookings at once |
| 5 | `/auth/*`: register, login, refresh, logout, logout-all, profile, loyalty | ✅ done | Same bcrypt hashes, JWTs, `refreshToken` cookie and `refresh_token_sessions` rows, so a session moves freely between the two APIs |
| 6a | Admin foundation and `/admin/reviews` | ✅ done | Role checks (`requireAdmin`, `requireManager`), and the audit log: every successful admin write gets an `admin_audit_log` row before the response, or a 500 if that row cannot be written |
| 6b | `/admin/pricing`, `/loyalty`, `/notifications`, `/expenses`, `/maintenance`, `/deliveries` | 🚧 in progress (`/admin/pricing`, `/admin/loyalty`, `/admin/notifications`, `/admin/expenses` done) | Self-contained groups on their own tables |
| 6c | The rest of `/admin` (dashboard, reservations, cars and images, customers, messages, blog, settings, invoices) and `/upload` | ⏳ | Admin reservation writes take the same `GET_LOCK` as customer bookings; images go to object storage |
| 7 | Retire Express | ⏳ | Move schema migrations into this repo |

## Rules while both run

- **Same database, same schema.** Schema changes go through the Express migrations
  until step 7.
- **Same secrets.** `JWT_SECRET` must match so access tokens work on both sides.
- **Same JSON.** Every moved route keeps its URL, status codes, error messages and
  snake_case response shape. The integration tests pin that contract, and the parity
  tests prove it against the real Express API (see below).
- **Shared locks for shared writes.** Customer bookings move in step 4, while admin
  bookings, status changes and extensions stay on Express until step 6. Until then both
  APIs write `reservations`, so both take the same MySQL `GET_LOCK`
  (`nexorarent:reservation:{carId}`) and lock the car and overlapping rows in a
  transaction. A parity test books one car on both APIs at once and expects one booking.

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
PARITY_EXPRESS_URL=http://localhost:4000 PARITY_DOTNET_URL=http://localhost:5080 \
  dotnet test --project tests/NexoraRent.ParityTests
```

Express reads `DATETIME` values as UTC (mysql2 `timezone: 'Z'`). Versions before that
used the Node process time zone, so with an older Express start it with `TZ=UTC`.

### Write routes

`WriteParityTests` sends each write to Express, puts the database back the way it was,
sends the same write to .NET, and compares the responses and the stored rows (generated
ids, booking numbers and timestamps excepted). They also book the same car on both APIs at
the same moment and expect exactly one booking, which proves the two share the lock.

They need two more variables: `PARITY_JWT_SECRET` (the `JWT_SECRET` both APIs use, to sign
customer tokens) and `PARITY_DB` (a connection string to their database). Use a separate
database with the Express migrations, seed and `Sql/fixtures.sql`, never your development
data. Express allows 20 requests to `/reservations` per IP every 15 minutes and the suite
sends 18, so restart Express before running it twice in a row.

## Known differences from Express

| Area | Express | This API | Why |
|---|---|---|---|
| Numbers | `DECIMAL` columns as strings (`"35.00"`) | JSON numbers (`35.0`) | Matches the client's TypeScript types; `Number()` in the client handles both |
| Booleans | `0` / `1` | `true` / `false` | Same reason |
| `limit` (cars, blog) | Unbounded | Clamped to 1–100 | Prevents very large pages |
| Search | `%` and `_` act as wildcards | Treated as literal text | Predictable search |
| 4xx messages in production | Replaced with "Internal server error" by the error handler | Real message | Client can show a useful error |
| `requestId` in error bodies | Only when the error goes through `next(err)` | On every error body, equal to the `X-Request-Id` header | Every reported error can be found in the logs. Parity tests skip this key, since each server generates its own id |
| Booking errors in production (409 "Car is not available", ...) | Replaced with "Internal server error" by the error handler | Real message | Same reason as the row above; the booking page can say why |
| Malformed JSON body | 400 with the parser's message | 400 "Request body is not valid JSON" | Parser messages differ per runtime |
| Clearing the refresh cookie (logout, bad refresh) | Express 4 `clearCookie` with `maxAge` sets an **empty cookie that lasts 7 days** | Expires the cookie (`expires=1970`) | The cookie is really gone. Both sides treat an empty cookie as no token, so the client sees no difference |
| Session times | `CURRENT_TIMESTAMP` (MySQL session time zone) | `UTC_TIMESTAMP()` | `expires_at` is written in UTC by both APIs, so comparing in UTC stays right whatever time zone MySQL runs in |
| Sign-in with an unknown email | Answers at once | Runs one bcrypt check first | Same 401 either way, but the response time no longer tells whether an account exists |
| `PUT /admin/reviews/:id/reply` with a non-string `admin_reply` | 500 (`trim` is not a function) | 400 "admin_reply is required" | A bad body is the client's error |
| `page` / `limit` on admin lists | Unbounded; a non-number makes the query fail | Page at least 1, limit clamped to 1–100 | Same rule as the public lists |
| Two sign-ups with one email at the same moment | The second hits the unique key and returns 500 | 409 "Email already registered" | The unique key decides, and the loser gets the normal answer |
| `POST`/`PUT /admin/pricing/rules` with an unknown `type`, a `multiplier` outside 0–99.99, a bad date, or an unknown `car_id` | MySQL rejects the row and the answer is 500 | 400 with the field's message ("Car not found" for the car) | A bad body is the client's error |
| `conditions` sent as a string | Stored as a JSON string, so the rule never applies | 400 "conditions must be an object" | Such a rule silently did nothing; the admin page now has to send an object |
| `PUT /admin/pricing/rules/:id` for a missing rule | 200 with `{}` | 404 "Pricing rule not found" | The client can tell the rule is gone |
| `GET /admin/pricing/calculate` with an unreadable date or a missing car | 500 (`NaN` dates, or reading `price_per_day` of undefined) | 400 "pickup_date and dropoff_date must be dates" / 404 "Car not found" | Same reason |
| `PUT /admin/loyalty/accounts/:id/adjust` with `points` that is not a whole number, is 0, or is beyond ±1,000,000 | `parseInt` keeps the leading digits (`2.5` → 2, `"0"` logs a 0-point change); text that is not a number is a 500 | 400 ("points required" for 0) | A typo cannot move points silently |
| Two point adjustments to one account at the same moment | Read, add and write back separately, so one can be lost | The account row is locked for the change, so both count | Points are money to the customer |
| `PUT /admin/loyalty/rewards/:id` for a missing reward | 200 with `{}` | 404 "Reward not found" | Same as pricing rules |
| A loyalty write with an unknown `type`/`min_tier`, a non-positive `value`/`points_cost`, or a description over the column size | MySQL rejects it and the answer is 500 | 400 with the field's message | A bad body is the client's error |
| `POST /admin/notifications/send` with a `reservation_id` that does not exist | Fills the template without the booking, then the log row fails its foreign key: 500 | 404 "Reservation not found", nothing logged | The admin sees the booking id is wrong |
| `POST /admin/notifications/send` for a booking | The log row has no `user_id`, so the log shows no customer name for it | The booking's customer is logged | The log says who was messaged |
| `POST /admin/notifications/send` with a non-text `type` or `recipient` | `recipient.replace` throws: 500 (a numeric `type` found no template: 200 `success: false`) | 400 "… must be text …" | A bad body is the client's error |
| `PUT /admin/notifications/templates/:id` for a missing template, or with `template_body` empty or null | 200 with `{}`, or MySQL rejects NULL: 500 | 404 "Template not found" / 400 | Same as the other admin groups; a blank template would send empty messages |
| `GET /admin/notifications/log` `page`/`limit` | Unbounded; a non-number makes the query fail | Page at least 1, limit clamped to 1–100 (default 30) | Same rule as the other admin lists |
| `POST`/`PUT /admin/expenses` with an unknown `car_id` or `category`, an `amount` that is not a number or beyond ±99,999,999.99, or an `expense_date` that is not `YYYY-MM-DD` | MySQL rejects the row (500); `parseFloat` keeps the leading digits of `"12abc"` | 400 ("Car not found" for the car) | A bad body is the client's error |
| Order of expenses on the same day, and of equal totals in `/admin/expenses/summary` | Left to MySQL, so pages can overlap | Broken by id (category for the totals) | Stable pages |
| `/admin/expenses` `page`/`limit` | `parseInt`, unbounded; a non-number makes the query fail | Page at least 1, limit clamped to 1–100 (default 20) | Same rule as the other admin lists |
