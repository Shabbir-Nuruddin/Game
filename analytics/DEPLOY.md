# Deploying the analytics service

The reasoning that used to live in `vercel.json` lives here instead: **that file
cannot hold comments.** Vercel validates it against a strict schema and rejects
any key it does not recognise — including the `"//"` convention that works in
most other JSON tooling — and the deployment fails at build time with
`should NOT have additional property`.

## Why `builds` / `routes` and not `rewrites`

Vercel's zero-config detection sees a `public/` directory and concludes the
project is a static site. It publishes `public/` to the CDN and never builds the
serverless function, which produces three symptoms at once:

- `/healthz` and `/api/*` return Vercel's own 404 — the request never reaches
  Express;
- the dashboard loads anyway, served straight off the CDN;
- and it loads **without its password prompt**, because Express's auth
  middleware is never in the request path.

Declaring `builds` switches zero-config off entirely. That is what this project
wants: `server/index.js` already serves `public/` itself, behind auth, so Vercel
must not serve it separately. One route sends everything to the Express app and
it does its own routing — identical to running `npm start` locally.

## Why `includeFiles`

`@vercel/node` decides what to bundle by tracing `require()` calls.
`express.static()` reads `public/` at runtime rather than requiring it, so
without `includeFiles` that folder is simply absent from the deployment and the
dashboard 404s while the API works.

## Setup

1. **Database** — create a Neon project and copy the **Pooled** connection
   string (the host contains `-pooler`). The direct string will exhaust
   connections: on serverless every warm lambda holds its own pool.
2. **Import** the repo on vercel.com, and set **Root Directory** to
   `analytics`. Missing this step is the most common failure — Vercel otherwise
   tries to build the Unity project at the repo root.
3. **Environment variables** (Production):
   - `DATABASE_URL` — the pooled Neon string
   - `DASH_USER` / `DASH_PASS` — dashboard login. **Do not leave these blank.**
     `auth()` treats unset as "local dev, stay open", which would publish the
     dashboard and the raw player data to anyone with the URL.
4. **Deploy**, then open `/healthz`.

No schema step: the tables create themselves on the first request into a cold
container (see `ready()` in `server/db.js`).

## Reading `/healthz`

```json
{ "ok": true, "database_url_set": true, "pooled": true,
  "database": "connected", "tables": 3 }
```

| Symptom | Cause |
| --- | --- |
| Vercel's 404 page instead of JSON | the function is not in the request path — check Root Directory and that this `vercel.json` deployed |
| `database_url_set: false` | env var missing, or not applied to Production |
| `pooled: false` | direct Neon string; swap to the `-pooler` host |
| `database: "unreachable"` | the `error` field carries the real cause |
| `tables: 0` | connected, but schema creation failed |
| dashboard loads with no password prompt | `DASH_USER` / `DASH_PASS` are blank |

## Pointing the game at it

Two constants, and they must share one origin:

- `Assets/Scripts/Analytics.cs` → `Endpoint`, ending in `/collect`
- `Assets/Scripts/Leaderboard.cs` → `Host`, no path

## Keeping inside the free tier

An `events` row plus its four indexes is roughly 450 bytes, so 0.5 GB is about
1.2 million events. The retention query is documented at the bottom of
`server/schema.sql`; run it in Neon's SQL Editor when storage gets tight.
