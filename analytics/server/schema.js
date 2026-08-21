// One-shot schema creation.
//
//   npm run schema
//
// On a long-lived server the app calls init() at boot, so this is only needed
// for serverless: Vercel has no boot step, and running CREATE TABLE IF NOT
// EXISTS in front of the first request of every cold lambda is a wasted
// round-trip on every one of them.
//
// Safe to run repeatedly — schema.sql is entirely IF NOT EXISTS.
require('dotenv').config();
const { init, pool } = require('./db');

init()
  .then(() => {
    console.log('Schema created / already present.');
    return pool.end();
  })
  .then(() => process.exit(0))
  .catch((e) => {
    console.error('Schema creation failed:', e.message);
    process.exit(1);
  });
