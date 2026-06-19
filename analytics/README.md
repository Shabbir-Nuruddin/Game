# Trust Issues — Live Analytics

Our own analytics pipeline for the game (not Unity's). Three pieces:

1. **Game client** — `Assets/Scripts/Analytics.cs` + `Assets/Plugins/Beacon.jslib`. Queues gameplay
   events and sends them in batches over HTTPS. Anonymous (a random per-device id, no names/emails).
2. **Server** — `analytics/server/` (Node + Express). Ingests events into Postgres and exposes
   read-only aggregation APIs.
3. **Dashboard** — `analytics/public/` (HTML + Chart.js). Charts the data.

```
 Game (Unity WebGL)  --POST /collect-->  Server (Express)  -->  Postgres (Neon)
                                              |
        Dashboard (Chart.js)  <--GET /api/*---
```

## What gets tracked
`session_start`, `mode_start`, `level_start`, `level_complete`, `death` (with cause + time on
level), `checkpoint`, `run_end` (completed / out_of_lives / quit / versus), `pause`, `resume`,
`versus_result`, and a `heartbeat` every 15s. From these the dashboard derives: unique players,
sessions, mode popularity, deaths per level, **time spent per level**, the **drop-off funnel**
(how far players get), what kills players, and sessions per day.

---

## Go live — step by step

### 1. Create the database (free, permanent) — Neon
1. Sign up at **https://neon.tech** (free, no card).
2. **Create project** → it gives you a **connection string** like
   `postgresql://user:pass@ep-xxx.neon.tech/neondb?sslmode=require`. Copy it.

### 2. Run the server locally to verify
```bash
cd analytics/server
cp .env.example .env        # then edit .env:
#   DATABASE_URL=...the Neon string...
#   DASH_USER=admin
#   DASH_PASS=something-secret
npm install
npm start
```
Open **http://localhost:3000**, log in with `DASH_USER`/`DASH_PASS` → an empty dashboard.
The `events` table is created automatically on first boot.

### 3. Smoke-test with the game (Unity Editor)
1. In `Assets/Scripts/Analytics.cs`, keep `Endpoint = "http://localhost:3000/collect"`.
2. Press **Play** in Unity and play for a bit (die, finish a level, switch modes).
3. Within ~5s, events appear at **http://localhost:3000/api/recent** and the charts fill in.
   (The Editor can use plain `http`. The live build must use `https` — see below.)

### 4. Deploy the server (free, public, HTTPS) — Render
1. Push this repo to GitHub (it already is a git repo): commit the `analytics/` folder.
2. Go to **https://render.com** → **New → Web Service** → connect this GitHub repo.
3. Settings:
   - **Root Directory:** `analytics/server`
   - **Build Command:** `npm install`
   - **Start Command:** `npm start`
   - **Environment variables:** `DATABASE_URL` (the Neon string), `DASH_USER`, `DASH_PASS`.
4. Deploy. Render gives you a public HTTPS URL, e.g.
   `https://trust-issues-analytics.onrender.com`.

### 5. Point the live game at it
1. In `Assets/Scripts/Analytics.cs` set:
   ```csharp
   public const string Endpoint = "https://trust-issues-analytics.onrender.com/collect";
   ```
   > Must be **https** — Unity Play / itch.io serve the game over https, and a browser blocks an
   > https page from calling an http server (mixed content). CORS is already open (`*`) so it works
   > from any host, Unity Play or itch.io.
2. **Rebuild the WebGL build** in Unity and re-upload it to Unity Play (and later itch.io).
3. Play the live game → open your Render URL → log in → **live analytics**.

### 6. (Recommended) keep the server warm
Render's free tier sleeps after ~15 min idle, so the first request after a nap is slow. No data is
lost (the game queues + retries, and uses `sendBeacon` on tab close), but to minimize cold starts add
a free uptime ping at **https://cron-job.org** or **uptimerobot.com** hitting
`https://<your-app>.onrender.com/healthz` every 10 minutes.

---

## Endpoints
| Method | Path | Auth | Purpose |
|---|---|---|---|
| POST | `/collect` | open (CORS *) | game sends event batches |
| GET | `/healthz` | open | uptime ping |
| GET | `/` | Basic auth | dashboard |
| GET | `/api/overview` `/modes` `/deaths-by-level` `/time-by-level` `/funnel` `/death-causes` `/sessions-daily` `/recent` | Basic auth | dashboard data |

## Notes
- **Privacy:** only an anonymous random device id + gameplay events. No personal data.
- **Cost:** Neon free + Render free = $0. Both are persistent (Neon keeps your data across restarts).
- **Switching hosts (itch.io etc.):** nothing to change server-side — CORS is `*`. Just keep the
  same `Endpoint` in the game.
