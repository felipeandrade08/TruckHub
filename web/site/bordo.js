/* TransPoli • TransPoli — Computador de bordo (web).
   Espelha o aplicativo Windows usando os dados da API:
   viagem ativa, telemetria, despesas, documentos e eventos operacionais. */

const API_BASE = window.TRANSPOLI_API_URL || 'https://api.transpoli.com.br';
const POLL_MS = 4000;
const STALE_MS = 30000; // sem amostra nova nesse tempo = ETS2 desconectado

const $ = (s, root = document) => root.querySelector(s);
const esc = (v) => String(v ?? '').replace(/[&<>'"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#039;', '"': '&quot;' }[c]));
const num = (v, digits = 0) => Number(v || 0).toLocaleString('pt-BR', { maximumFractionDigits: digits });
const money = (v) => Number(v || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
const dateTime = (v) => (v ? new Date(v).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : '—');

const state = {
  trips: [],
  expenses: [],
  events: [],
  documents: [],
  trip: null,
  telemetry: null,
  summary: null,
  connected: false,
};

async function api(path, options = {}) {
  const response = await fetch(`${API_BASE}${path}`, {
    ...options,
    credentials: 'include',
    headers: { 'content-type': 'application/json', ...(options.headers || {}) },
  });
  const data = await response.json().catch(() => ({}));
  if (response.status === 401) {
    window.location.href = 'login.html';
    throw new Error('Sessão expirada.');
  }
  if (!response.ok) throw new Error(data.error || 'Não foi possível concluir a operação.');
  return data;
}

async function safe(path) {
  try {
    return await api(path);
  } catch (error) {
    if (String(error.message).includes('Sessão')) throw error;
    return null;
  }
}

/* ---------------- render ---------------- */

function gearLabel(gear) {
  const g = Number(gear || 0);
  if (g === 0) return 'N';
  if (g < 0) return `R${Math.abs(g) > 1 ? Math.abs(g) : ''}`;
  return String(g);
}

function duration(startedAt, finishedAt) {
  if (!startedAt) return '00:00:00';
  const end = finishedAt ? new Date(finishedAt).getTime() : Date.now();
  let seconds = Math.max(0, Math.floor((end - new Date(startedAt).getTime()) / 1000));
  const h = String(Math.floor(seconds / 3600)).padStart(2, '0');
  const m = String(Math.floor((seconds % 3600) / 60)).padStart(2, '0');
  const s = String(seconds % 60).padStart(2, '0');
  return `${h}:${m}:${s}`;
}

function renderConnection() {
  const pill = $('[data-connection]');
  const text = $('[data-connection-text]');
  const lock = $('[data-lock]');
  pill.classList.toggle('is-online', state.connected);
  text.textContent = state.connected ? 'ETS2 CONECTADO' : 'ETS2 DESCONECTADO';
  const unlocked = state.connected && state.trip?.status === 'active';
  lock.classList.toggle('is-unlocked', unlocked);
  lock.textContent = unlocked ? '🔓 CAMINHÃO LIBERADO' : '🔒 CAMINHÃO BLOQUEADO';
}

function renderVehicle() {
  const t = state.telemetry;
  const trip = state.trip;
  const truckName = [trip?.truckBrand || trip?.truck_brand, trip?.truckModel || trip?.truck_model]
    .filter(Boolean).join(' ').trim();

  $('[data-truck-name]').textContent = truckName || (trip ? 'Caminhão não identificado' : 'Aguardando ETS2');
  $('[data-route-text]').textContent = trip
    ? `${trip.origin || trip.sourceCompany || 'Origem'} → ${trip.destination || trip.destinationCompany || 'Destino'}`
    : 'Conecte o ETS2 para receber a telemetria.';

  const engine = $('[data-engine]');
  const running = Boolean(t && !t.gamePaused && (t.rpm > 0 || t.speedKph > 0));
  engine.textContent = running ? 'LIGADO' : t?.gamePaused ? 'JOGO PAUSADO' : 'DESLIGADO';
  engine.classList.toggle('on', running);

  $('[data-speed]').textContent = num(t?.speedKph, 0);
  $('[data-rpm]').textContent = num(t?.rpm, 0);
  $('[data-gear]').textContent = gearLabel(t?.gear);
  $('[data-cruise]').textContent = t && t.speedKph > 40 && running ? 'AUTO' : 'OFF';
  $('[data-odometer]').textContent = `${num(t?.odometerKm, 1)} km`;
  $('[data-fuel]').textContent = `${num(t?.fuelL, 1)} L`;
  $('[data-range]').textContent = `${num(t?.fuelRangeKm, 0)} km`;
}

function renderOps() {
  const trip = state.trip;
  const summary = state.summary;

  $('[data-trip-status]').textContent = trip
    ? trip.status === 'active' ? 'Viagem em andamento' : 'Última viagem registrada'
    : 'Aguardando trabalho do ETS2';
  $('[data-trip-route]').textContent = trip
    ? `${trip.origin || 'Origem'} → ${trip.destination || 'Destino'}`
    : 'Nenhuma viagem ativa';
  $('[data-trip-cargo]').textContent = trip?.cargo ? `Carga: ${trip.cargo}` : '';

  const distance = summary?.distance_km ?? trip?.distance_km ?? 0;
  $('[data-trip-distance]').textContent = `${num(distance, 1)} km`;
  $('[data-trip-duration]').textContent = duration(trip?.started_at, trip?.finished_at);

  $('[data-cargo]').textContent = trip?.cargo || 'Nenhuma carga';
  const mass = state.cargoMassKg;
  $('[data-cargo-mass]').textContent = mass ? `${num(mass, 0)} kg` : '0 kg';

  const unlock = $('[data-unlock]');
  unlock.disabled = !trip || trip.status !== 'active';
  unlock.textContent = trip?.status === 'active' ? '🏁 FINALIZAR VIAGEM' : '🔓 DESBLOQUEAR CAMINHÃO';
}

function renderCounters() {
  const fuelings = state.expenses.filter((x) => x.type === 'fuel').length;
  const stops = state.events.filter((x) => String(x.event_type || '').includes('stop')).length;
  const occurrences = state.events.filter((x) => String(x.event_type || '').includes('occurrence')).length;
  const documents = state.documents.length;
  $('[data-counters]').textContent =
    `⛽ ${fuelings} abastecimentos • 🛑 ${stops} paradas • ⚠ ${occurrences} ocorrências • 📄 ${documents} documentos`;
}

function renderAlerts() {
  const alert = $('[data-alert]');
  const t = state.telemetry;
  alert.className = 'alert-text';

  if (!state.connected) {
    alert.textContent = 'Telemetria offline • abra o TransPoli no PC com o ETS2';
    alert.classList.add('warn');
  } else if (t && t.fuelRangeKm > 0 && t.fuelRangeKm < 80) {
    alert.textContent = '⛽ AUTONOMIA BAIXA • planeje abastecimento';
    alert.classList.add('danger');
  } else if (t && t.speedKph > 90) {
    alert.textContent = '⚠ VELOCIDADE ALTA • reduza para rodar seguro';
    alert.classList.add('warn');
  } else {
    alert.textContent = 'Nenhum alerta ativo';
  }

  $('[data-telemetry-info]').textContent = state.telemetry
    ? `Última amostra recebida em ${dateTime(state.telemetry.recordedAt)}`
    : 'Telemetria aguardando conexão';
  $('[data-fuel-auto]').textContent = state.telemetry
    ? `Abastecimento automático: monitorando • ${num(state.telemetry.fuelL, 1)} L no tanque`
    : 'Abastecimento automático: aguardando telemetria';

  $('[data-status]').textContent = state.connected
    ? `TransPoli • telemetria ativa • ${state.trip?.status === 'active' ? 'viagem em andamento' : 'aguardando viagem'}`
    : 'TransPoli pronto. Inicie o Connector junto com o ETS2.';
}

function renderAll() {
  renderConnection();
  renderVehicle();
  renderOps();
  renderCounters();
  renderAlerts();
}

/* ---------------- carga de dados ---------------- */

async function loadTrip() {
  const trips = (await safe('/me/trips'))?.trips || [];
  state.trips = trips;
  state.trip = trips.find((x) => x.status === 'active') || trips[0] || null;

  if (!state.trip) {
    state.telemetry = null;
    state.summary = null;
    state.documents = [];
    state.connected = false;
    return;
  }

  const id = encodeURIComponent(state.trip.id);
  const [live, summary, documents] = await Promise.all([
    safe(`/me/trips/${id}/telemetry`),
    safe(`/me/trips/${id}/summary`),
    safe(`/me/trips/${id}/documents`),
  ]);

  state.telemetry = live?.telemetry || null;
  state.cargoMassKg = live?.trip?.cargoMassKg ?? state.trip.cargo_mass_kg ?? null;
  if (live?.trip) {
    state.trip.truckBrand = live.trip.truckBrand;
    state.trip.truckModel = live.trip.truckModel;
    state.trip.sourceCompany = live.trip.sourceCompany;
    state.trip.destinationCompany = live.trip.destinationCompany;
  }
  state.summary = summary?.summary || null;
  state.documents = documents?.documents || [];

  const recordedAt = state.telemetry ? new Date(state.telemetry.recordedAt).getTime() : 0;
  state.connected = state.trip.status === 'active' && Date.now() - recordedAt < STALE_MS;
}

async function loadSupport() {
  const [expenses, events] = await Promise.all([safe('/me/expenses'), safe('/me/events?limit=100')]);
  state.expenses = expenses?.expenses || [];
  state.events = events?.events || [];
}

async function refresh() {
  try {
    await Promise.all([loadTrip(), loadSupport()]);
    renderAll();
  } catch (error) {
    console.error('bordo_refresh_error', error);
  }
}

/* ---------------- painéis ---------------- */

function openSheet(kicker, title, intro, rows) {
  $('[data-sheet-kicker]').textContent = kicker;
  $('[data-sheet-title]').textContent = title;
  $('[data-sheet-intro]').textContent = intro || '';
  $('[data-sheet-body]').innerHTML = rows.length
    ? rows.map((r) => `<div class="sheet-row"><div><b>${esc(r.title)}</b><small>${esc(r.detail || '')}</small></div><span>${esc(r.value || '')}</span></div>`).join('')
    : '<p class="sheet-empty">Nenhum registro encontrado ainda.</p>';
  $('[data-sheet]').hidden = false;
}

function closeSheet() {
  $('[data-sheet]').hidden = true;
}

const labels = { fuel: 'Combustível', toll: 'Pedágio', maintenance: 'Manutenção', parking: 'Estacionamento', food: 'Alimentação', other: 'Outro' };

function openPanel(panel) {
  if (panel === 'home') {
    refresh();
    return;
  }

  if (panel === 'fuel') {
    const rows = state.expenses.filter((x) => x.type === 'fuel').map((x) => ({
      title: x.description || 'Abastecimento',
      detail: dateTime(x.created_at),
      value: money(x.amount),
    }));
    openSheet('PAINEL RÁPIDO', 'Abastecimentos', 'Abastecimentos registrados na sua conta TransPoli.', rows);
    return;
  }

  if (panel === 'stop' || panel === 'occurrence') {
    const key = panel === 'stop' ? 'stop' : 'occurrence';
    const rows = state.events.filter((x) => String(x.event_type || '').includes(key)).map((x) => ({
      title: x.event_type,
      detail: dateTime(x.occurred_at),
      value: '',
    }));
    openSheet(
      'PAINEL RÁPIDO',
      panel === 'stop' ? 'Paradas' : 'Ocorrências',
      'Registros sincronizados pelo computador de bordo do PC. Os lançamentos feitos direto no aplicativo Windows aparecem aqui após a sincronização.',
      rows,
    );
    return;
  }

  if (panel === 'document') {
    const rows = state.documents.map((x) => ({
      title: x.title || x.document_type,
      detail: dateTime(x.created_at),
      value: 'OK',
    }));
    openSheet('DOCUMENTAÇÃO', 'Documentos da viagem', 'Documentos gerados para a viagem atual. Gere e imprima pelo painel completo.', rows);
    return;
  }

  if (panel === 'summary') {
    const s = state.summary;
    const t = state.trip;
    const rows = [
      { title: 'Viagem ativa', detail: '', value: t?.status === 'active' ? 'SIM' : 'NÃO' },
      { title: 'Distância registrada', detail: '', value: `${num(s?.distance_km ?? t?.distance_km, 1)} km` },
      { title: 'Combustível consumido', detail: '', value: `${num(s?.fuel_used_l ?? t?.fuel_used_l, 1)} L` },
      { title: 'Despesas da viagem', detail: '', value: money(s?.expensesBrl) },
      { title: 'Valor do job', detail: '', value: s?.cargo_value_brl == null ? '—' : money(s.cargo_value_brl) },
      { title: 'Resultado', detail: '', value: s?.resultBrl == null ? '—' : money(s.resultBrl) },
      { title: 'Último odômetro', detail: '', value: `${num(state.telemetry?.odometerKm, 1)} km` },
    ];
    openSheet('OPERAÇÃO', 'Resumo operacional TransPoli', '', rows);
  }
}

async function unlockOrFinish() {
  const trip = state.trip;
  if (!trip) return;
  if (trip.status !== 'active') return;
  if (!confirm(`Finalizar a viagem ${trip.origin || 'Origem'} → ${trip.destination || 'Destino'}?`)) return;
  const button = $('[data-unlock]');
  button.disabled = true;
  try {
    await api(`/me/trips/${encodeURIComponent(trip.id)}/finish`, { method: 'POST', body: JSON.stringify({}) });
    await refresh();
  } catch (error) {
    alert(error.message);
  } finally {
    button.disabled = false;
  }
}

/* ---------------- init ---------------- */

document.querySelectorAll('[data-panel]').forEach((b) => b.addEventListener('click', () => openPanel(b.dataset.panel)));
$('[data-unlock]').addEventListener('click', unlockOrFinish);
$('[data-sheet-close]').addEventListener('click', closeSheet);
$('[data-sheet]').addEventListener('click', (e) => { if (e.target === e.currentTarget) closeSheet(); });
document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeSheet(); });

setInterval(() => {
  if (state.trip?.status === 'active') $('[data-trip-duration]').textContent = duration(state.trip.started_at, null);
}, 1000);

refresh();
setInterval(refresh, POLL_MS);
