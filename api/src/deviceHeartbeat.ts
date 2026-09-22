import { neon } from '@neondatabase/serverless'
import { registerDeviceTelemetryRoutes } from './deviceTelemetry'

const SESSION_COOKIE = 'truckhub_session'
const DEVICE_ID_MIN = 16
const DEVICE_ID_MAX = 255

function getCookie(request: Request, name: string) {
  const header = request.headers.get('Cookie') ?? ''
  for (const part of header.split(';')) {
    const [key, ...value] = part.trim().split('=')
    if (key === name) return value.join('=')
  }
  return null
}

function getBearerToken(request: Request) {
  const header = request.headers.get('Authorization') ?? ''
  if (!header.toLowerCase().startsWith('bearer ')) return null
  const token = header.slice(7).trim()
  return token || null
}

async function hashSessionToken(token: string) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(token))
  let binary = ''
  for (const byte of new Uint8Array(digest)) binary += String.fromCharCode(byte)
  return btoa(binary)
}

function jsonError(message: string, status: number, code?: string) {
  return new Response(JSON.stringify({ ok: false, error: message, ...(code ? { code } : {}) }), {
    status,
    headers: { 'content-type': 'application/json; charset=UTF-8', 'cache-control': 'no-store' }
  })
}

export function registerDeviceHeartbeatRoutes(app: any) {
  app.post('/me/device/heartbeat', async (c: any) => {
    if (!c.env.DATABASE_URL) return jsonError('Banco de dados não configurado.', 500)

    const token = getBearerToken(c.req.raw) ?? getCookie(c.req.raw, SESSION_COOKIE)
    if (!token) return jsonError('Sessão inválida ou expirada.', 401, 'SESSION_INVALID')

    const body = await c.req.json().catch(() => ({}))
    const deviceId = typeof body?.deviceId === 'string' ? body.deviceId.trim() : ''
    if (!deviceId || deviceId.length < DEVICE_ID_MIN || deviceId.length > DEVICE_ID_MAX) {
      return jsonError('Identificador do dispositivo inválido.', 400, 'INVALID_DEVICE_ID')
    }

    try {
      const tokenHash = await hashSessionToken(token)
      const sql = neon(c.env.DATABASE_URL)
      const users = await sql`
        SELECT u.id AS user_id, l.id AS license_id, l.status AS license_status,
               l.license_type, l.trial_expires_at,
               d.id AS device_id, d.device_id AS bound_device_id, d.status AS device_status
        FROM sessions s
        JOIN users u ON u.id = s.user_id
        JOIN licenses l ON l.user_id = u.id
        LEFT JOIN devices d ON d.license_id = l.id AND d.status = 'active'
        WHERE s.token_hash = ${tokenHash}
          AND s.revoked_at IS NULL
          AND s.expires_at > NOW()
          AND u.status = 'active'
        LIMIT 1
      `
      const current = users[0]
      if (!current) return jsonError('Sessão inválida ou expirada.', 401, 'SESSION_INVALID')

      const isValidTrial = current.license_status === 'trial' &&
        current.trial_expires_at && new Date(current.trial_expires_at).getTime() > Date.now()
      const isActiveLifetime = current.license_status === 'active' && current.license_type === 'lifetime'

      if (!isValidTrial && !isActiveLifetime) {
        if (current.license_status === 'trial') {
          await sql`
            UPDATE licenses
            SET status = 'expired', updated_at = NOW()
            WHERE id = ${current.license_id}
              AND status = 'trial'
              AND trial_expires_at <= NOW()
          `
        }
        return jsonError('Licença expirada ou inativa.', 403, 'LICENSE_INACTIVE')
      }

      if (!current.device_id || current.device_status !== 'active') {
        return jsonError('Este computador não está vinculado a esta licença.', 403, 'DEVICE_NOT_BOUND')
      }

      const updated = await sql`
        UPDATE devices
        SET last_seen_at = NOW()
        WHERE id = ${current.device_id}
          AND license_id = ${current.license_id}
          AND device_id = ${deviceId}
          AND status = 'active'
        RETURNING id, device_id, device_name, status, last_seen_at
      `
      if (!updated[0]) return jsonError('Este computador não está vinculado a esta licença.', 403, 'DEVICE_NOT_BOUND')

      await sql`
        UPDATE sessions
        SET last_seen_at = NOW()
        WHERE token_hash = ${tokenHash}
          AND revoked_at IS NULL
          AND expires_at > NOW()
          AND (last_seen_at IS NULL OR last_seen_at < NOW() - INTERVAL '15 minutes')
      `

      return c.json({
        ok: true,
        license: { type: current.license_type, status: current.license_status },
        device: updated[0],
        serverTime: new Date().toISOString()
      }, 200, { 'cache-control': 'no-store' })
    } catch (error) {
      console.error('device_heartbeat_error', error)
      return jsonError('Erro interno ao validar o dispositivo.', 500, 'HEARTBEAT_ERROR')
    }
  })

  // Central de Motoristas — presença baseada na última telemetria ao vivo.
  // Um motorista fica online enquanto envia telemetria válida nos últimos 30 segundos.
  app.get('/me/drivers/online', async (c: any) => {
    const token = getBearerToken(c.req.raw) ?? getCookie(c.req.raw, SESSION_COOKIE)
    if (!token) return jsonError('Sessão inválida ou expirada.', 401, 'SESSION_INVALID')
    try {
      const tokenHash = await hashSessionToken(token)
      const sql = neon(c.env.DATABASE_URL)
      const users = await sql`SELECT u.id FROM sessions s JOIN users u ON u.id=s.user_id
        WHERE s.token_hash=${tokenHash} AND s.revoked_at IS NULL AND s.expires_at>NOW()
          AND u.status='active' AND s.session_type IN ('web','desktop') LIMIT 1`
      if (!users[0]) return jsonError('Sessão inválida ou expirada.', 401, 'SESSION_INVALID')

      const rows = await sql`
        SELECT u.id AS user_id, u.name,
               d.recorded_at, d.connected, d.game, d.speed_kph, d.game_paused,
               d.truck_brand, d.truck_model, d.license_plate, d.cargo,
               d.source_city, d.destination_city, d.on_job, d.refuel_active,
               t.trip_id AS active_trip_id, t.trip_cargo, t.trip_origin, t.trip_destination,
               t.start_odometer_km, t.current_odometer_km
        FROM device_telemetry_latest d
        JOIN users u ON u.id=d.user_id
        LEFT JOIN LATERAL (
          SELECT tr.id AS trip_id, tr.cargo AS trip_cargo, tr.origin AS trip_origin,
                 tr.destination AS trip_destination,
                 COALESCE(
                   NULLIF(tr.start_odometer_km, 0),
                   (SELECT MIN(s0.odometer_km) FROM trip_telemetry_samples s0 WHERE s0.trip_id=tr.id)
                 ) AS start_odometer_km,
                 (
                   SELECT s.odometer_km
                   FROM trip_telemetry_samples s
                   WHERE s.trip_id=tr.id
                   ORDER BY s.recorded_at DESC
                   LIMIT 1
                 ) AS current_odometer_km
          FROM trips tr
          WHERE tr.user_id=d.user_id AND tr.status='active'
          ORDER BY tr.started_at DESC
          LIMIT 1
        ) t ON true
        WHERE u.status='active'
          AND d.recorded_at >= NOW() - INTERVAL '30 seconds'
          AND d.connected = true
        ORDER BY d.recorded_at DESC, u.name ASC
      `

      const drivers = rows.map((r: any) => ({
        id: r.user_id,
        name: r.name || 'Motorista',
        truck: [r.truck_brand, r.truck_model].filter(Boolean).join(' ') || 'Caminhão não identificado',
        plate: r.license_plate || null,
        cargo: r.cargo || 'Sem carga',
        origin: r.source_city || '—',
        destination: r.destination_city || '—',
        speedKph: Number(r.speed_kph) || 0,
        tripKm: Number.isFinite(Number(r.current_odometer_km)) && Number.isFinite(Number(r.start_odometer_km))
          ? Math.max(0, Number(r.current_odometer_km) - Number(r.start_odometer_km))
          : 0,
        status: r.refuel_active ? 'ABASTECENDO' : r.game_paused ? 'PAUSADO' : r.on_job ? 'EM VIAGEM' : 'DISPONÍVEL',
        recordedAt: r.recorded_at,
      }))
      return c.json({ ok: true, online: drivers.length, drivers }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('online_drivers_error', error)
      return jsonError('Erro ao consultar a Central de Motoristas.', 500)
    }
  })

  // Keep live telemetry under the same registration point so the API bootstrap
  // does not need a second route registration path.
  registerDeviceTelemetryRoutes(app)
}
