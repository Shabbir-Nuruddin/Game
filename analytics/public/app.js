// Dashboard logic: fetch each /api endpoint and render a chart.
// Charts are created once, then updated in place on refresh.

const COLORS = {
  blood: '#e23b3b', bloodDk: '#7a1622', purple: '#7a2bb0',
  gold: '#e0b33a', mid: '#9a8aa3', grid: 'rgba(255,255,255,.07)',
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

// Create or update a chart by id.
function draw(id, config) {
  if (charts[id]) {
    charts[id].data = config.data;
    charts[id].update();
  } else {
    charts[id] = new Chart($(id), config);
  }
}

const baseOpts = (extra = {}) => ({
  responsive: true,
  maintainAspectRatio: false,
  scales: {
    x: { grid: { color: COLORS.grid } },
    y: { grid: { color: COLORS.grid }, beginAtZero: true },
  },
  ...extra,
});

function renderKpis(o) {
  const m = Math.round((o.avg_session_seconds || 0) / 6) / 10; // minutes, 1dp
  const cards = [
    ['Unique players', o.players],
    ['Sessions', o.sessions],
    ['Runs started', o.runs],
    ['Total deaths', o.deaths],
    ['Active today', o.dau],
    ['Avg session', m + ' min'],
  ];
  $('kpis').innerHTML = cards
    .map(([l, n]) => `<div class="kpi"><div class="n">${n ?? 0}</div><div class="l">${l}</div></div>`)
    .join('');
}

// Group a flat [{mode, level, value}] list into one dataset per mode.
function byMode(rows, valueKey, levelKey = 'level') {
  const levels = [...new Set(rows.map((r) => r[levelKey]))].sort((a, b) => a - b);
  const modes = [...new Set(rows.map((r) => r.mode))];
  const datasets = modes.map((mode) => ({
    label: mode,
    backgroundColor: MODE_COLOR[mode] || COLORS.mid,
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
    rows
      .map((r) => {
        const t = new Date(r.received_at).toLocaleTimeString();
        const sid = (r.session_id || '').slice(0, 6);
        const props = JSON.stringify(r.props || {});
        return `<tr><td>${t}</td><td><span class="tag">${r.name}</span></td>
                <td>${sid}</td><td class="props">${props}</td></tr>`;
      })
      .join('');
}

async function refresh() {
  const status = $('status');
  try {
    status.textContent = 'refreshing…';
    status.className = 'status';

    const [overview, modes, deaths, time, funnel, causes, daily, recent] = await Promise.all([
      getJSON('api/overview'),
      getJSON('api/modes'),
      getJSON('api/deaths-by-level'),
      getJSON('api/time-by-level'),
      getJSON('api/funnel'),
      getJSON('api/death-causes'),
      getJSON('api/sessions-daily'),
      getJSON('api/recent'),
    ]);

    renderKpis(overview);

    draw('modes', {
      type: 'doughnut',
      data: {
        labels: modes.map((m) => m.mode),
        datasets: [{ data: modes.map((m) => m.runs),
          backgroundColor: modes.map((m) => MODE_COLOR[m.mode] || COLORS.mid) }],
      },
      options: { responsive: true, maintainAspectRatio: false },
    });

    draw('funnel', {
      type: 'bar',
      data: {
        labels: funnel.map((f) => 'Lv ' + (f.level + 1)),
        datasets: [{ label: 'sessions reaching', data: funnel.map((f) => f.sessions),
          backgroundColor: COLORS.purple }],
      },
      options: baseOpts({ plugins: { legend: { display: false } } }),
    });

    draw('deaths', {
      type: 'bar',
      data: byMode(deaths, 'deaths'),
      options: baseOpts({ scales: { x: { stacked: true, grid: { color: COLORS.grid } },
        y: { stacked: true, beginAtZero: true, grid: { color: COLORS.grid } } } }),
    });

    draw('time', {
      type: 'bar',
      data: byMode(time, 'avg_seconds'),
      options: baseOpts(),
    });

    draw('causes', {
      type: 'bar',
      data: {
        labels: causes.map((c) => c.cause),
        datasets: [{ label: 'deaths', data: causes.map((c) => c.deaths),
          backgroundColor: COLORS.blood }],
      },
      options: baseOpts({ indexAxis: 'y', plugins: { legend: { display: false } } }),
    });

    draw('daily', {
      type: 'line',
      data: {
        labels: daily.map((d) => d.day),
        datasets: [
          { label: 'sessions', data: daily.map((d) => d.sessions),
            borderColor: COLORS.blood, backgroundColor: COLORS.blood, tension: 0.3 },
          { label: 'players', data: daily.map((d) => d.players),
            borderColor: COLORS.gold, backgroundColor: COLORS.gold, tension: 0.3 },
        ],
      },
      options: baseOpts(),
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
