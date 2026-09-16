(() => {
  const API_BASE = window.TRUCKHUB_API_URL || 'http://localhost:8787';
  const cache = new Map();
  let lastSignature = '';

  const esc = (value) => String(value ?? '').replace(/[&<>'\"]/g, (c) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#039;', '"': '&quot;'
  }[c]));
  const num = (value) => Number(value || 0);
  const km = (value) => `${num(value).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} km`;
  const liters = (value) => `${num(value).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} L`;
  const hours = (seconds) => {
    const total = Math.max(0, Math.round(num(seconds)));
    const h = Math.floor(total / 3600);
    const m = Math.floor((total % 3600) / 60);
    return `${h}h ${String(m).padStart(2, '0')}min`;
  };

  async function getTacho(tripId) {
    if (cache.has(tripId)) return cache.get(tripId);
    const response = await fetch(`${API_BASE}/me/tachographs/${encodeURIComponent(tripId)}`, {
      credentials: 'include'
    });
    const data = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(data.error || 'Não foi possível carregar o tacógrafo.');
    const tacho = data.tachograph || null;
    cache.set(tripId, tacho);
    return tacho;
  }

  function ensureContent(box) {
    let content = box.querySelector('[data-tacho-content]');
    if (!content) {
      content = document.createElement('div');
      content.dataset.tachoContent = '';
      box.appendChild(content);
    }
    return content;
  }

  async function render() {
    const trips = window.__truckhubTrips || [];
    const finished = trips.filter((trip) => trip.status === 'finished');
    const signature = finished.map((trip) => `${trip.id}:${trip.finished_at || ''}`).join('|');
    if (!signature && lastSignature === 'empty') return;
    if (signature === lastSignature) return;
    lastSignature = signature || 'empty';

    const box = document.querySelector('#tacografo');
    if (!box) return;
    const content = ensureContent(box);

    if (!finished.length) {
      content.innerHTML = '<div class="tacho-grid"><div class="detail-item"><span>VIAGENS COM TACÓGRAFO</span><strong>0</strong></div><div class="detail-item"><span>DISTÂNCIA</span><strong>0 km</strong></div><div class="detail-item"><span>TEMPO DE DIREÇÃO</span><strong>0h 00min</strong></div><div class="detail-item"><span>STATUS</span><strong>AGUARDANDO VIAGEM</strong></div></div><p class="tacho-note">Inicie uma viagem com o ETS2 conectado. O TruckHub calculará direção, paradas, pausas, distância e consumo a partir da telemetria.</p>';
      return;
    }

    content.innerHTML = '<p class="tacho-note">Atualizando dados reais da telemetria...</p>';
    const results = await Promise.all(finished.map(async (trip) => {
      try { return await getTacho(trip.id); } catch { return null; }
    }));
    const tachos = results.filter(Boolean);

    const totalDriving = tachos.reduce((sum, t) => sum + num(t.driving_seconds), 0);
    const totalStopped = tachos.reduce((sum, t) => sum + num(t.stopped_seconds), 0);
    const totalPaused = tachos.reduce((sum, t) => sum + num(t.paused_seconds), 0);
    const totalDistance = tachos.reduce((sum, t) => sum + num(t.distance_km), 0);
    const totalFuel = tachos.reduce((sum, t) => sum + num(t.fuel_used_l), 0);
    const maxSpeed = tachos.reduce((max, t) => Math.max(max, num(t.max_speed_kph)), 0);
    const average = totalDriving > 0 ? totalDistance / (totalDriving / 3600) : 0;

    content.innerHTML = `<div class="tacho-grid">
      <div class="detail-item"><span>VIAGENS PROCESSADAS</span><strong>${tachos.length}/${finished.length}</strong></div>
      <div class="detail-item"><span>TEMPO DE DIREÇÃO</span><strong>${hours(totalDriving)}</strong></div>
      <div class="detail-item"><span>TEMPO PARADO</span><strong>${hours(totalStopped)}</strong></div>
      <div class="detail-item"><span>TEMPO PAUSADO</span><strong>${hours(totalPaused)}</strong></div>
      <div class="detail-item"><span>DISTÂNCIA</span><strong>${km(totalDistance)}</strong></div>
      <div class="detail-item"><span>COMBUSTÍVEL CONSUMIDO</span><strong>${liters(totalFuel)}</strong></div>
      <div class="detail-item"><span>VELOCIDADE MÉDIA</span><strong>${num(average).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} km/h</strong></div>
      <div class="detail-item"><span>VELOCIDADE MÁXIMA</span><strong>${num(maxSpeed).toLocaleString('pt-BR', { maximumFractionDigits: 1 })} km/h</strong></div>
    </div><p class="tacho-note">Dados calculados pelas amostras reais da telemetria do ETS2. O TruckHub separa automaticamente direção, parada e jogo pausado.</p>`;
  }

  function observe() {
    render();
    setInterval(() => {
      const trips = window.__truckhubTrips || [];
      const signature = trips.filter((trip) => trip.status === 'finished').map((trip) => `${trip.id}:${trip.finished_at || ''}`).join('|') || 'empty';
      if (signature !== lastSignature) render();
    }, 1500);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', observe, { once: true });
  else observe();
})();
