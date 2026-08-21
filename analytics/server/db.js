// Postgres connection pool. Works with Neon (cloud) and a local Postgres.
const { Pool } = require('pg');
const fs = require('fs');
const path = require('path');

if (!process.env.DATABASE_URL) {
  // On a serverless host, process.exit() kills the whole invocation and the
  // caller sees an opaque 500 with nothing in the log. Throw instead: the error
  // handler reports it and the function stays diagnosable.
  const msg = 'FATAL: DATABASE_URL is not set.';
  console.error(msg);
  if (!process.env.VERCEL) process.exit(1);
  throw new Error(msg);
}

// Neon (and most hosted Postgres) require SSL; a plain local Postgres does not.
const isLocal = /localhost|127\.0\.0\.1/.test(process.env.DATABASE_URL);

// CONNECTION COUNT IS THE THING THAT KILLS SERVERLESS POSTGRES.
//
// On a long-lived server (Render) one process holds one pool and `max: 5` is
// five connections total, forever. On Vercel every concurrent invocation is its
// own Node context with its own pool, so the same number means five connections
// PER WARM LAMBDA — a couple of hundred players landing at once will exhaust
// Neon's connection limit and every request starts failing at the database.
//
// Two things prevent it: a pool of 1 per invocation, and Neon's POOLED
// connection string (the host with `-pooler` in it), which fronts the database
// with PgBouncer so short-lived lambdas share a small set of real connections.
// Use the pooled string in Vercel's DATABASE_URL — this is not optional.
const pool = new Pool({
  connectionString: process.env.DATABASE_URL,
  ssl: isLocal ? false : { rejectUnauthorized: false },
  max: process.env.VERCEL ? 1 : 5,
  idleTimeoutMillis: process.env.VERCEL ? 10000 : 30000,
});

// Create the table + indexes if they don't exist yet (safe to run every boot).
async function init() {
  const schema = fs.readFileSync(path.join(__dirname, 'schema.sql'), 'utf8');
  await pool.query(schema);
  console.log('Database ready.');
}

// ---- SCHEMA, WITHOUT A MANUAL STEP ---------------------------------------
//
// A long-lived server calls init() once at boot. A serverless host has no boot,
// which originally meant the tables had to be created by hand in the database
// provider's web console — a step that depends on someone finding a SQL editor
// in a UI that gets redesigned, and that fails confusingly if they don't.
//
// So the schema creates itself, lazily, guarded by a module-scope promise. The
// promise lives as long as the container does, so this runs ONCE per cold start
// rather than once per request — a handful of times an hour at low traffic, and
// less than that under load. schema.sql is entirely IF NOT EXISTS, so every run
// after the first is a no-op that costs one round-trip.
//
// The failure is cached too: if the database is unreachable the rejection is
// what every caller sees, and the next cold start retries from scratch.
let _ready = null;

/// <summary>Resolves once the tables are guaranteed to exist.</summary>
function ready() {
  if (_ready === null) {
    _ready = init().catch((e) => {
      _ready = null;          // let the next request try again
      throw e;
    });
  }
  return _ready;
}

module.exports = { pool, init, ready };
