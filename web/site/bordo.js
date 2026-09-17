/* TransPoli • Computador de bordo web.
   Espelha o estado ao vivo do aplicativo Windows e, quando existir, a viagem ativa. */

const API_BASE = window.TRANSPOLI_API_URL || 'https://truckhub.felipe-pessoall2026.workers.dev';
const POLL_MS = 4000;
const STALE_MS = 30000;
const $ = (s, root = document) => root.querySelector(s);
const esc = (v) => String(v ?? '').replace(/[&<>'\"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#039;', '"': '&quot;' }[c]));
const num = (v, digits = 0) => Number(v || 0).toLocaleString('pt-BR', { maximumFractionDigits: digits });
const money = (v) => Number(v || 0).toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' });
const dateTime = (v) => (v ? new Date(v).toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' }) : '—');

const state = { trips: [], expenses: [], events: [], documents: [], trip: null, telemetry: null, summary: null, connected: false, cargoMassKg: null };

async function api(path, options = {}) {
  const response = await fetch(`${API_BASE}${path}`, { ...options, credentials: 'include', headers: { 'content-type': 'application/json', ...(options.headers || {}) } });
  const data = await response.json().catch(() => ({}));
  if (response.status === 401) { window.location.href = 'login.html'; throw new Error('Sessão expirada.'); }
  if (!response.ok) throw new Error(data.error || 'Não foi possível concluir a operação.');
  return data;
}
async function safe(path) { try { return await api(path); } catch (error) { if (String(error.message).includes('Sessão')) throw error; return null; } }
function gearLabel(gear) { const g = Number(gear || 0); if (g === 0) return 'N'; if (g < 0) return `R${Math.abs(g) > 1 ? Math.abs(g) : ''}`; return String(g); }
function duration(startedAt, finishedAt) { if (!startedAt) return '00:00:00'; const end = finishedAt ? new Date(finishedAt).getTime() : Date.now(); const seconds = Math.max(0, Math.floor((end - new Date(startedAt).getTime()) / 1000)); return `${String(Math.floor(seconds / 3600)).padStart(2, '0')}:${String(Math.floor((seconds % 3600) / 60)).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`; }

function renderConnection() {
  const pill = $('[data-connection]'), text = $('[data-connection-text]'), lock = $('[data-lock]');
  if (!pill || !text || !lock) return;
  pill.classList.toggle('is-online', state.connected); text.textContent = state.connected ? 'ETS2 CONECTADO' : 'ETS2 DESCONECTADO';
  const t = state.telemetry; const stopped = !t || Math.abs(Number(t.speedKph || 0)) < 0.5; const unlocked = state.connected && Boolean(t?.engineEnabled) && !stopped && state.trip?.status === 'active';
  lock.classList.toggle('is-unlocked', unlocked); lock.textContent = unlocked ? '🔓 CAMINHÃO LIBERADO' : '🔒 CAMINHÃO BLOQUEADO';
}

function renderVehicle() {
  const t = state.telemetry, trip = state.trip;
  const truckName = [t?.truckBrand || trip?.truckBrand || trip?.truck_brand, t?.truckModel || trip?.truckModel || trip?.truck_model].filter(Boolean).join(' ').trim();
  $('[data-truck-name]').textContent = truckName || (trip ? 'Caminhão não identificado' : 'Aguardando ETS2');
  $('[data-route-text]').textContent = t?.sourceCity || t?.destinationCity ? `${t.sourceCity || 'Origem'} → ${t.destinationCity || 'Destino'}` : trip ? `${trip.origin || trip.sourceCompany || 'Origem'} → ${trip.destination || trip.destinationCompany || 'Destino'}` : 'Conecte o ETS2 para receber a telemetria.';
  const engine = $('[data-engine]'), running = Boolean(t && !t.gamePaused && t.engineEnabled); engine.textContent = running ? 'LIGADO' : t?.gamePaused ? 'JOGO PAUSADO' : 'DESLIGADO'; engine.classList.toggle('on', running);
  $('[data-speed]').textContent = num(t?.speedKph, 0); $('[data-rpm]').textContent = num(t?.rpm, 0); $('[data-gear]').textContent = gearLabel(t?.gear);
  $('[data-cruise]').textContent = t?.cruiseControl ? 'AUTO' : 'OFF'; $('[data-odometer]').textContent = `${num(t?.odometerKm, 1)} km`; $('[data-fuel]').textContent = `${num(t?.fuelL, 1)} L`; $('[data-range]').textContent = `${num(t?.fuelRangeKm, 0)} km`;
}

function renderOps() {
  const trip = state.trip, summary = state.summary;
  $('[data-trip-status]').textContent = trip ? trip.status === 'active' ? 'Viagem em andamento' : 'Última viagem registrada' : 'Aguardando trabalho do ETS2';
  $('[data-trip-route]').textContent = trip ? `${trip.origin || 'Origem'} → ${trip.destination || 'Destino'}` : 'Nenhuma viagem ativa';
  $('[data-trip-cargo]').textContent = trip?.cargo ? `Carga: ${trip.cargo}` : '';
  $('[data-trip-distance]').textContent = `${num(summary?.distance_km ?? trip?.distance_km ?? 0, 1)} km`;
  $('[data-trip-duration]').textContent = duration(trip?.started_at, trip?.finished_at);
  $('[data-cargo]').textContent = state.telemetry?.cargo || trip?.cargo || 'Nenhuma carga';
  $('[data-cargo-mass]').textContent = state.telemetry?.cargoMassKg ? `${num(state.telemetry.cargoMassKg, 0)} kg` : state.cargoMassKg ? `${num(state.cargoMassKg, 0)} kg` : '0 kg';
  const unlock = $('[data-unlock]'); if (!unlock) return; unlock.disabled = !trip || trip.status !== 'active'; unlock.textContent = trip?.status === 'active' ? '🏁 FINALIZAR VIAGEM' : '🔓 DESBLOQUEAR CAMINHÃO';
}
function renderCounters() {
  const fuelings = state.expenses.filter((x) => x.type === 'fuel').length, stops = state.events.filter((x) => String(x.event_type || '').includes('stop')).length, occurrences = state.events.filter((x) => String(x.event_type || '').includes('occurrence')).length;
  $('[data-counters]').textContent = `⛽ ${fuelings} abastecimentos • 🛑 ${stops} paradas • ⚠ ${occurrences} ocorrências • 📄 ${state.documents.length} documentos`;
}
function renderAlerts() {
  const alert = $('[data-alert]'), t = state.telemetry; alert.className = 'alert-text';
  if (!state.connected) { alert.textContent = 'Telemetria offline • abra o TransPoli no PC com o ETS2'; alert.classList.add('warn'); }
  else if (t?.airPressureEmergency) { alert.textContent = '🛑 PRESSÃO DE AR EMERGÊNCIA'; alert.classList.add('danger'); }
  else if (t?.airPressureWarning) { alert.textContent = '⚠ PRESSÃO DE AR BAIXA'; alert.classList.add('warn'); }
  else if (t?.fuelWarning || (t?.fuelRangeKm > 0 && t.fuelRangeKm < 80)) { alert.textContent = '⛽ AUTONOMIA BAIXA • planeje abastecimento'; alert.classList.add('danger'); }
  else if (t?.speedKph > 90) { alert.textContent = '⚠ VELOCIDADE ALTA • reduza para rodar seguro'; alert.classList.add('warn'); }
  else alert.textContent = 'Nenhum alerta ativo';
  $('[data-telemetry-info]').textContent = t ? `Última amostra ${dateTime(t.recordedAt)} • motor ${t.engineEnabled ? 'LIGADO' : 'DESLIGADO'} • ar ${num(t.airPressure, 1)} psi • freio ${num(t.brakeTemperature, 0)}°C` : 'Telemetria aguardando conexão';
  $('[data-fuel-auto]').textContent = t ? `Abastecimento automático: monitorando • ${num(t.fuelL, 1)} L no tanque` : 'Abastecimento automático: aguardando telemetria';
  $('[data-status]').textContent = state.connected ? `TransPoli • telemetria ativa • ${state.trip?.status === 'active' ? 'viagem em andamento' : 'aguardando viagem'}` : 'TransPoli pronto. Inicie o Connector junto com o ETS2.';
}
function renderAll() { renderConnection(); renderVehicle(); renderOps(); renderCounters(); renderAlerts(); }

async function loadTrip() {
  const [tripData, liveData] = await Promise.all([safe('/me/trips'), safe('/me/device/telemetry')]);
  const trips = tripData?.trips || []; state.trips = trips; state.trip = trips.find((x) => x.status === 'active') || trips[0] || null;
  const deviceTelemetry = liveData?.telemetry || null;
  if (!state.trip) { state.telemetry = deviceTelemetry; state.summary = null; state.documents = []; state.cargoMassKg = null; state.connected = Boolean(deviceTelemetry && Date.now() - new Date(deviceTelemetry.recordedAt).getTime() < STALE_MS); return; }
  const id = encodeURIComponent(state.trip.id);
  const [tripLive, summary, documents] = await Promise.all([safe(`/me/trips/${id}/telemetry`), safe(`/me/trips/${id}/summary`), safe(`/me/trips/${id}/documents`)]);
  const tripTelemetry = tripLive?.telemetry || null;
  state.telemetry = { ...(tripTelemetry || {}), ...(deviceTelemetry || {}) };
  state.cargoMassKg = deviceTelemetry?.cargoMassKg ?? tripLive?.trip?.cargoMassKg ?? state.trip.cargo_mass_kg ?? null;
  if (tripLive?.trip) { state.trip.truckBrand = tripLive.trip.truckBrand; state.trip.truckModel = tripLive.trip.truckModel; state.trip.sourceCompany = tripLive.trip.sourceCompany; state.trip.destinationCompany = tripLive.trip.destinationCompany; }
  state.summary = summary?.summary || null; state.documents = documents?.documents || [];
  const recordedAt = state.telemetry ? new Date(state.telemetry.recordedAt).getTime() : 0; state.connected = Boolean(state.telemetry) && Date.now() - recordedAt < STALE_MS;
}
async function loadSupport() { const [expenses, events] = await Promise.all([safe('/me/expenses'), safe('/me/events?limit=100')]); state.expenses = expenses?.expenses || []; state.events = events?.events || []; }
async function refresh() { try { await Promise.all([loadTrip(), loadSupport()]); renderAll(); } catch (error) { console.error('bordo_refresh_error', error); } }

function openSheet(kicker, title, intro, rows) { $('[data-sheet-kicker]').textContent = kicker; $('[data-sheet-title]').textContent = title; $('[data-sheet-intro]').textContent = intro || ''; $('[data-sheet-body]').innerHTML = rows.length ? rows.map((r) => `<div class="sheet-row"><div><b>${esc(r.title)}</b><small>${esc(r.detail || '')}</small></div><span>${esc(r.value || '')}</span></div>`).join('') : '<p class="sheet-empty">Nenhum registro encontrado ainda.</p>'; $('[data-sheet]').hidden = false; }
function closeSheet() { $('[data-sheet]').hidden = true; }
function openPanel(panel) {
  if (panel === 'home') { refresh(); return; }
  if (panel === 'fuel') { openSheet('PAINEL RÁPIDO', 'Abastecimentos', 'Abastecimentos registrados na sua conta TransPoli.', state.expenses.filter((x) => x.type === 'fuel').map((x) => ({ title: x.description || 'Abastecimento', detail: dateTime(x.created_at), value: money(x.amount) }))); return; }
  if (panel === 'stop' || panel === 'occurrence') { const key = panel === 'stop' ? 'stop' : 'occurrence'; openSheet('PAINEL RÁPIDO', panel === 'stop' ? 'Paradas' : 'Ocorrências', 'Registros sincronizados pelo computador de bordo do PC.', state.events.filter((x) => String(x.event_type || '').includes(key)).map((x) => ({ title: x.event_type, detail: dateTime(x.occurred_at), value: '' }))); return; }
  if (panel === 'document') { openSheet('DOCUMENTAÇÃO', 'Documentos da viagem', 'Documentos gerados para a viagem atual.', state.documents.map((x) => ({ title: x.title || x.document_type, detail: dateTime(x.created_at), value: 'OK' }))); return; }
  if (panel === 'summary') { const s = state.summary, t = state.trip; openSheet('OPERAÇÃO', 'Resumo operacional TransPoli', '', [{ title: 'Viagem ativa', detail: '', value: t?.status === 'active' ? 'SIM' : 'NÃO' }, { title: 'Distância registrada', detail: '', value: `${num(s?.distance_km ?? t?.distance_km, 1)} km` }, { title: 'Combustível consumido', detail: '', value: `${num(s?.fuel_used_l ?? t?.fuel_used_l, 1)} L` }, { title: 'Despesas da viagem', detail: '', value: money(s?.expensesBrl) }, { title: 'Valor do job', detail: '', value: s?.cargo_value_brl == null ? '—' : money(s.cargo_value_brl) }, { title: 'Último odômetro', detail: '', value: `${num(state.telemetry?.odometerKm, 1)} km` }, { title: 'Motor', detail: '', value: state.telemetry?.engineEnabled ? 'LIGADO' : 'DESLIGADO' }]); }
}
async function unlockOrFinish() { const trip = state.trip; if (!trip || trip.status !== 'active') return; if (!confirm(`Finalizar a viagem ${trip.origin || 'Origem'} → ${trip.destination || 'Destino'}?`)) return; const button = $('[data-unlock]'); button.disabled = true; try { await api(`/me/trips/${encodeURIComponent(trip.id)}/finish`, { method: 'POST', body: JSON.stringify({}) }); await refresh(); } catch (error) { alert(error.message); } finally { button.disabled = false; } }

document.querySelectorAll('[data-panel]').forEach((b) => b.addEventListener('click', () => openPanel(b.dataset.panel)));
$('[data-unlock]').addEventListener('click', unlockOrFinish); $('[data-sheet-close]').addEventListener('click', closeSheet); $('[data-sheet]').addEventListener('click', (e) => { if (e.target === e.currentTarget) closeSheet(); }); document.addEventListener('keydown', (e) => { if (e.key === 'Escape') closeSheet(); });
setInterval(() => { if (state.trip?.status === 'active') $('[data-trip-duration]').textContent = duration(state.trip.started_at, null); }, 1000);
refresh(); setInterval(refresh, POLL_MS);
