// Vercel entry point.
//
// Vercel serves every file in /api as its own serverless function. vercel.json
// rewrites ALL paths to this one, so the existing Express app keeps owning its
// own routing (/collect, /score, /leaderboard, /echo, /echoes, /healthz, /api/*
// and the dashboard) exactly as it does when running locally on Render or a VPS.
//
// Keeping one function rather than splitting each route into its own file means
// there is exactly one copy of the server to reason about, and local `npm start`
// and production stay the same program.
module.exports = require('../server/index.js');
