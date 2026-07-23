// Dashboard logic: fetch each /api endpoint, render KPI cards + charts.
// Charts are created once and updated in place on refresh.

const COLORS = {
  blood: '#e23b3b', bloodDk: '#7a1622', purple: '#7a2bb0',
  gold: '#e0b33a', green: '#3ec97a', mid: '#9a8aa3', grid: 'rgba(255,255,255,.07)',
};
const MODE_COLOR = { Curated: '#8e7bb0', Endless: '#7a2bb0', Daily: '#e23b3b', Versus: '#e0b33a' };

Chart.defaults.color = '#9a8aa3';
Chart.defaults.font.family = 'Segoe UI, Roboto, sans-serif';
Chart.defaults.plugins.legend.labels.boxWidth = 12;

const charts = {};
const $ = (id) => document.getElementById(id);

async function getJSON(path) {
  const r = await fetch(path, { headers: { Accept: 'application/json' } });
  if (!r.ok) throw new Error(path + ' -> ' + r.status);
  return r.json();
}

function draw(id, config) {
  if (charts[id]) { charts[id].data = config.data; charts[id].options = config.options; charts[id].update(); }
  else charts[id] = new Chart($(id), config);
}

const baseOpts = (extra = {}) => ({
  responsive: true, maintainAspectRatio: false,
  scales: { x: { grid: { color: COLORS.grid } }, y: { grid: { color: COLORS.grid }, beginAtZero: true } },
  ...extra,
});

const fmtSecs = (s) => (!s ? '0s' : s < 60 ? s + 's' : Math.floor(s / 60) + 'm ' + (s % 60) + 's');

// Escape anything player-controlled before it touches innerHTML. Event name and
// props come from the OPEN /collect endpoint, so a crafted event could otherwise
// inject <script>/<img onerror> that runs in the dashboard (stored XSS).
const esc = (v) =>
  String(v).replace(/[&<>"']/g, (c) =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

// One KPI card. `bench` (optional) shows a target + colors the value vs it.
function kpiCard({ label, value, sub, bench }) {
  let cls = '', badge = '';
  if (bench) {
    const v = parseFloat(value);
    const ok = !isNaN(v) && v >= bench.good;
    const warn = !isNaN(v) && v >= bench.ok;
    cls = isNaN(v) ? '' : ok ? 'good' : warn ? 'warn' : 'bad';
    badge = `<div class="bench">target ${bench.label}</div>`;
  }
  return `<div class="kpi ${cls}">
    <div class="n">${value}</div>
    <div class="l">${label}</div>
    ${sub ? `<div class="sub2">${sub}</div>` : ''}${badge}
  </div>`;
}

const pctOrDash = (row) => (!row || row.pct == null ? '—' : row.pct + '%');

function renderActive(k) {
  const stick = k.mau ? Math.round((k.dau / k.mau) * 100) : 0;
  $('kpi-active').innerHTML = [
    kpiCard({ label: 'New today', value: k.new_today ?? 0 }),
    kpiCard({ label: 'DAU', value: k.dau ?? 0, sub: 'active today' }),
    kpiCard({ label: 'WAU', value: k.wau ?? 0, sub: 'last 7 days' }),
    kpiCard({ label: 'MAU', value: k.mau ?? 0, sub: 'last 30 days' }),
    kpiCard({ label: 'Stickiness', value: stick + '%', sub: 'DAU / MAU', bench: { good: 50, ok: 20, label: '>20%' } }),
    kpiCard({ label: 'Total players', value: k.players ?? 0, sub: 'all time' }),
  ].join('');
}

function renderRetention(curve) {
  const at = (d) => curve.find((r) => r.day === d);
  $('kpi-retention').innerHTML = [
    kpiCard({ label: 'D1 retention', value: pctOrDash(at(1)), sub: 'back next day', bench: { good: 40, ok: 20, label: '>40%' } }),
    kpiCard({ label: 'D7 retention', value: pctOrDash(at(7)), sub: 'back after a week', bench: { good: 20, ok: 10, label: '>20%' } }),
    kpiCard({ label: 'D30 retention', value: pctOrDash(at(30)), sub: 'back after a month', bench: { good: 10, ok: 5, label: '>10%' } }),
  ].join('');

  draw('retentionCurve', {
    type: 'line',
    data: {
      labels: curve.map((r) => 'D' + r.day),
      datasets: [{
        label: '% retained', data: curve.map((r) => r.pct),
        borderColor: COLORS.green, backgroundColor: 'rgba(62,201,122,.15)',
        fill: true, tension: 0.3, spanGaps: true,
      }],
    },
    options: baseOpts({
      plugins: { legend: { display: false } },
      scales: { x: { grid: { color: COLORS.grid } }, y: { beginAtZero: true, max: 100, grid: { color: COLORS.grid }, ticks: { callback: (v) => v + '%' } } },
    }),
  });
}

function renderEngagement(k, daily) {
  const perPlayer = k.players ? (k.sessions / k.players).toFixed(1) : '0';
  const runsPerSession = k.sessions ? (k.runs / k.sessions).toFixed(1) : '0';
  $('kpi-engagement').innerHTML = [
    kpiCard({ label: 'Avg session', value: fmtSecs(k.avg_session_seconds || 0) }),
    kpiCard({ label: 'Sessions / player', value: perPlayer }),
    kpiCard({ label: 'Runs / session', value: runsPerSession }),
    kpiCard({ label: 'Total sessions', value: k.sessions ?? 0 }),
  ].join('');

  draw('activeDaily', {
    type: 'bar',
    data: {
      labels: daily.map((d) => d.day),
      datasets: [
        { label: 'new', data: daily.map((d) => d.new_players), backgroundColor: COLORS.blood },
        { label: 'returning', data: daily.map((d) => d.returning_players), backgroundColor: COLORS.purple },
      ],
    },
    options: baseOpts({ scales: {
      x: { stacked: true, grid: { color: COLORS.grid } },
      y: { stacked: true, beginAtZero: true, grid: { color: COLORS.grid } },
    } }),
  });
}

function renderMoney() {
  const defs = [
    ['ARPU', 'revenue ÷ all users'],
    ['ARPPU', 'revenue ÷ paying users'],
    ['Conversion', '% who pay (2–5% typical)'],
    ['LTV', 'lifetime value — must exceed CPI'],
    ['CPI', 'cost per install'],
  ];
  $('kpi-money').innerHTML = defs
    .map(([l, s]) => `<div class="kpi muted"><div class="n">—</div><div class="l">${l}</div><div class="sub2">${s}</div></div>`)
    .join('');
}

function byMode(rows, valueKey, levelKey = 'level') {
  const levels = [...new Set(rows.map((r) => r[levelKey]))].sort((a, b) => a - b);
  const modes = [...new Set(rows.map((r) => r.mode))];
  const datasets = modes.map((mode) => ({
    label: mode, backgroundColor: MODE_COLOR[mode] || COLORS.mid,
    data: levels.map((lv) => {
      const hit = rows.find((r) => r.mode === mode && r[levelKey] === lv);
      return hit ? Number(hit[valueKey]) : 0;
    }),
  }));
  return { labels: levels.map((l) => 'Lv ' + (l + 1)), datasets };
}

function renderRecent(rows) {
  const body = $('recent').querySelector('tbody');
  body.innerHTML =
    '<tr><th>time</th><th>event</th><th>session</th><th>props</th></tr>' +
    rows.map((r) => {
      const t = new Date(r.received_at).toLocaleTimeString();
      const sid = (r.session_id || '').slice(0, 6);
      const props = JSON.stringify(r.props || {});
      // esc() every player-controlled field — name, session id and props all
      // originate from the open ingest endpoint and are otherwise raw HTML here.
      return `<tr><td>${esc(t)}</td><td><span class="tag">${esc(r.name)}</span></td><td>${esc(sid)}</td><td class="props">${esc(props)}</td></tr>`;
    }).join('');
}

async function refresh() {
  const status = $('status');
  try {
    status.textContent = 'refreshing…';
    status.className = 'status';

    const [kpis, retention, daily, modes, deaths, time, funnel, causes, recent] = await Promise.all([
      getJSON('api/kpis'), getJSON('api/retention'), getJSON('api/active-daily'),
      getJSON('api/modes'), getJSON('api/deaths-by-level'), getJSON('api/time-by-level'),
      getJSON('api/funnel'), getJSON('api/death-causes'), getJSON('api/recent'),
    ]);

    renderActive(kpis);
    renderRetention(retention);
    renderEngagement(kpis, daily);
    renderMoney();

    draw('modes', {
      type: 'doughnut',
      data: { labels: modes.map((m) => m.mode), datasets: [{ data: modes.map((m) => m.runs), backgroundColor: modes.map((m) => MODE_COLOR[m.mode] || COLORS.mid) }] },
      options: { responsive: true, maintainAspectRatio: false },
    });
    draw('funnel', {
      type: 'bar',
      data: { labels: funnel.map((f) => 'Lv ' + (f.level + 1)), datasets: [{ label: 'sessions reaching', data: funnel.map((f) => f.sessions), backgroundColor: COLORS.purple }] },
      options: baseOpts({ plugins: { legend: { display: false } } }),
    });
    draw('deaths', {
      type: 'bar', data: byMode(deaths, 'deaths'),
      options: baseOpts({ scales: { x: { stacked: true, grid: { color: COLORS.grid } }, y: { stacked: true, beginAtZero: true, grid: { color: COLORS.grid } } } }),
    });
    draw('time', { type: 'bar', data: byMode(time, 'avg_seconds'), options: baseOpts() });
    draw('causes', {
      type: 'bar',
      data: { labels: causes.map((c) => c.cause), datasets: [{ label: 'deaths', data: causes.map((c) => c.deaths), backgroundColor: COLORS.blood }] },
      options: baseOpts({ indexAxis: 'y', plugins: { legend: { display: false } } }),
    });
    renderRecent(recent);

    status.textContent = 'updated ' + new Date().toLocaleTimeString();
  } catch (e) {
    status.textContent = 'error: ' + e.message;
    status.className = 'status err';
  }
}

$('refresh').addEventListener('click', refresh);
refresh();
setInterval(refresh, 30000);
