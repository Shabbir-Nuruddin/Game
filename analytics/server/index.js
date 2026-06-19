// Trust Issues — analytics collector + dashboard server.
//
//   POST /collect   <- the game sends batches of events here (open, CORS *)
//   GET  /api/*     <- the dashboard reads aggregated stats (password-protected)
//   GET  /          <- the dashboard itself (password-protected)
//   GET  /healthz   <- uptime ping (open)
//
require('dotenv').config(); // load .env locally (Render injects env vars directly)
const path = require('path');
const express = require('express');
const cors = require('cors');
const { pool, init } = require('./db');

const app = express();
app.set('trust proxy', true); // Render/Neon sit behind a proxy

// ---- Ingest -------------------------------------------------------------
// The game posts as text/plain (a "simple" CORS request -> no preflight),
// so we read the raw body and JSON.parse it ourselves.
app.use('/collect', cors());                                  // allow any game origin
app.options('/collect', cors());
app.use('/collect', express.text({ type: () => true, limit: '512kb' }));

app.post('/collect', async (req, res) => {
  let events;
  try {
    events = JSON.parse(req.body || '[]');
  } catch {
    return res.status(400).send('bad json'); // malformed -> don't ask client to retry
  }
  if (!Array.isArray(events)) events = [events];
  if (events.length === 0) return res.status(204).end();

  const origin = req.headers.origin || req.headers.referer || null;
  const rows = events.slice(0, 500); // cap a single batch

  const placeholders = [];
  const values = [];
  let i = 1;
  for (const e of rows) {
    placeholders.push(`($${i++},$${i++},$${i++},$${i++},$${i++},$${i++},$${i++})`);
    values.push(
      e.event_time || e.ts || null,
      e.device_id || null,
      e.session_id || null,
      String(e.name || 'unknown').slice(0, 64),
      JSON.stringify(e.props || {}),
      e.app_version || null,
      origin,
    );
  }

  try {
    await pool.query(
      `INSERT INTO events (event_time, device_id, session_id, name, props, app_version, origin)
       VALUES ${placeholders.join(',')}`,
      values,
    );
    res.status(204).end();
  } catch (err) {
    console.error('insert failed:', err.message);
    res.status(500).end(); // a real error -> client keeps the batch and retries
  }
});

app.get('/healthz', (_req, res) => res.json({ ok: true }));

// ---- Auth gate for everything below (dashboard + read APIs) -------------
function auth(req, res, next) {
  const user = process.env.DASH_USER;
  const pass = process.env.DASH_PASS;
  if (!user || !pass) return next(); // unset (local dev) -> open

  const [scheme, encoded] = (req.headers.authorization || '').split(' ');
  if (scheme === 'Basic' && encoded) {
    const [u, p] = Buffer.from(encoded, 'base64').toString().split(':');
    if (u === user && p === pass) return next();
  }
  res.set('WWW-Authenticate', 'Basic realm="Trust Issues Analytics"');
  return res.status(401).send('Authentication required.');
}

// ---- Read APIs ----------------------------------------------------------
const api = express.Router();
api.use(auth);

const q = (sql, params) => pool.query(sql, params).then((r) => r.rows);

// Headline KPIs: active users, engagement, totals. (Stickiness, sessions/player
// and runs/session are simple ratios the dashboard derives from these.)
api.get('/kpis', async (_req, res, next) => {
  try {
    const [r] = await q(`
      WITH firsts AS (
        SELECT device_id, min(received_at)::date AS d0
        FROM events WHERE device_id IS NOT NULL GROUP BY device_id
      ),
      sess AS (
        SELECT session_id, min(received_at) AS mn, max(received_at) AS mx
        FROM events WHERE session_id IS NOT NULL GROUP BY session_id
      )
      SELECT
        (SELECT count(*) FROM firsts)::int                                              AS players,
        (SELECT count(*) FROM firsts WHERE d0 = current_date)::int                      AS new_today,
        (SELECT count(DISTINCT device_id) FROM events WHERE received_at::date = current_date)::int AS dau,
        (SELECT count(DISTINCT device_id) FROM events WHERE received_at >= current_date - 6)::int  AS wau,
        (SELECT count(DISTINCT device_id) FROM events WHERE received_at >= current_date - 29)::int AS mau,
        (SELECT count(*) FROM sess)::int                                                AS sessions,
        (SELECT count(*) FROM events WHERE name = 'mode_start')::int                    AS runs,
        (SELECT count(*) FROM events WHERE name = 'death')::int                         AS deaths,
        (SELECT round(avg(extract(epoch FROM (mx - mn))))::int FROM sess)               AS avg_session_seconds
    `);
    res.json(r);
  } catch (e) { next(e); }
});

// Classic N-day retention curve: of the players whose FIRST day was >= N days ago,
// what % were active again exactly N days after they first showed up.
// Day 0 = 100% by definition. Empty cohorts return null (shown as "not enough data").
api.get('/retention', async (_req, res, next) => {
  try {
    res.json(await q(`
      WITH firsts AS (
        SELECT device_id, min(received_at)::date AS d0
        FROM events WHERE device_id IS NOT NULL GROUP BY device_id
      ),
      active AS (
        SELECT DISTINCT device_id, received_at::date AS d
        FROM events WHERE device_id IS NOT NULL
      ),
      days AS (SELECT generate_series(0, 30) AS x)
      SELECT dd.x AS day,
             count(DISTINCT f.device_id)::int AS cohort,
             count(DISTINCT a.device_id)::int AS retained,
             round(100.0 * count(DISTINCT a.device_id)
                   / NULLIF(count(DISTINCT f.device_id), 0), 1) AS pct
      FROM days dd
      LEFT JOIN firsts f ON f.d0 <= current_date - dd.x
      LEFT JOIN active a ON a.device_id = f.device_id AND a.d = f.d0 + dd.x
      GROUP BY dd.x ORDER BY dd.x
    `));
  } catch (e) { next(e); }
});

// Daily active users split into new vs returning players (engagement trend).
api.get('/active-daily', async (_req, res, next) => {
  try {
    res.json(await q(`
      WITH firsts AS (
        SELECT device_id, min(received_at)::date AS d0
        FROM events WHERE device_id IS NOT NULL GROUP BY device_id
      ),
      daily AS (
        SELECT received_at::date AS day, device_id
        FROM events WHERE device_id IS NOT NULL GROUP BY 1, 2
      )
      SELECT to_char(d.day, 'YYYY-MM-DD') AS day,
             count(*) FILTER (WHERE f.d0 = d.day)::int AS new_players,
             count(*) FILTER (WHERE f.d0 < d.day)::int AS returning_players,
             count(*)::int AS dau
      FROM daily d JOIN firsts f ON f.device_id = d.device_id
      GROUP BY d.day ORDER BY d.day
    `));
  } catch (e) { next(e); }
});

api.get('/modes', async (_req, res, next) => {
  try {
    res.json(await q(`
      SELECT coalesce(props->>'mode', 'unknown') AS mode, count(*)::int AS runs
      FROM events WHERE name = 'mode_start'
      GROUP BY 1 ORDER BY runs DESC`));
  } catch (e) { next(e); }
});

api.get('/deaths-by-level', async (_req, res, next) => {
  try {
    res.json(await q(`
      SELECT coalesce(props->>'mode','?') AS mode,
             (props->>'level_index')::int AS level,
             count(*)::int AS deaths
      FROM events
      WHERE name = 'death' AND props ? 'level_index'
      GROUP BY 1, 2 ORDER BY 1, 2`));
  } catch (e) { next(e); }
});

api.get('/time-by-level', async (_req, res, next) => {
  try {
    res.json(await q(`
      SELECT coalesce(props->>'mode','?') AS mode,
             (props->>'level_index')::int AS level,
             round(sum((props->>'duration_ms')::numeric) / 1000.0)::int        AS total_seconds,
             round(avg((props->>'duration_ms')::numeric) / 1000.0, 1)::float   AS avg_seconds
      FROM events
      WHERE name IN ('level_complete','death') AND props ? 'duration_ms'
      GROUP BY 1, 2 ORDER BY 1, 2`));
  } catch (e) { next(e); }
});

// How far each session got — the drop-off funnel.
api.get('/funnel', async (_req, res, next) => {
  try {
    res.json(await q(`
      SELECT level, count(*)::int AS sessions FROM (
        SELECT session_id, max((props->>'level_index')::int) AS level
        FROM events WHERE props ? 'level_index'
        GROUP BY session_id
      ) s GROUP BY level ORDER BY level`));
  } catch (e) { next(e); }
});

api.get('/death-causes', async (_req, res, next) => {
  try {
    res.json(await q(`
      SELECT coalesce(props->>'cause','unknown') AS cause, count(*)::int AS deaths
      FROM events WHERE name = 'death'
      GROUP BY 1 ORDER BY deaths DESC LIMIT 20`));
  } catch (e) { next(e); }
});

api.get('/recent', async (_req, res, next) => {
  try {
    res.json(await q(`
      SELECT received_at, name, session_id, device_id, props
      FROM events ORDER BY id DESC LIMIT 60`));
  } catch (e) { next(e); }
});

app.use('/api', api);

// ---- Dashboard (static) -------------------------------------------------
app.use('/', auth, express.static(path.join(__dirname, '..', 'public')));

// ---- Error handler ------------------------------------------------------
app.use((err, _req, res, _next) => {
  console.error(err);
  res.status(500).json({ error: 'server error' });
});

const PORT = process.env.PORT || 3000;
init()
  .then(() => app.listen(PORT, () => console.log(`Analytics server on :${PORT}`)))
  .catch((e) => { console.error('startup failed:', e); process.exit(1); });
