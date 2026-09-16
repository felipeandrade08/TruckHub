import { neon } from '@neondatabase/serverless'
import { hashSessionToken } from './sharedAuth'

const SESSION_COOKIE = 'truckhub_session'
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const TYPE_RE = /^[a-z0-9._-]{1,80}$/

function error(message: string, status: number) {
  return new Response(JSON.stringify({ ok: false, error: message }), {
    status,
    headers: { 'content-type': 'application/json; charset=UTF-8', 'cache-control': 'no-store' }
  })
}

function cookie(request: Request) {
  const raw = request.headers.get('Cookie') ?? ''
  for (const part of raw.split(';')) {
    const [key, ...value] = part.trim().split('=')
    if (key === SESSION_COOKIE) return decodeURIComponent(value.join('='))
  }
  return null
}

async function currentUser(c: any) {
  if (!c.env.DATABASE_URL) return null
  const bearer = c.req.header('Authorization')?.replace(/^Bearer\s+/i, '').trim()
  const token = bearer || cookie(c.req.raw)
  if (!token) return null
  const sql = neon(c.env.DATABASE_URL)
  const tokenHash = await hashSessionToken(token)
  const rows = await sql`
    SELECT u.id, u.name, u.email
    FROM sessions s
    JOIN users u ON u.id = s.user_id
    WHERE s.token_hash = ${tokenHash}
      AND s.revoked_at IS NULL
      AND s.expires_at > NOW()
      AND s.session_type = 'web'
      AND u.status = 'active'
    LIMIT 1
  `
  return rows[0] ?? null
}

export function registerTransPoliEventRoutes(app: any) {
  app.post('/me/events', async (c: any) => {
    try {
      const user = await currentUser(c)
      if (!user) return error('Sessão inválida ou expirada.', 401)

      const data = await c.req.json().catch(() => null) as any
      if (!data || typeof data !== 'object') return error('JSON inválido.', 400)

      const clientEventId = String(data.id ?? '').trim()
      const eventType = String(data.type ?? '').trim().toLowerCase()
      const tripId = data.tripId == null || data.tripId === '' ? null : String(data.tripId).trim()
      const payload = data.payload && typeof data.payload === 'object' && !Array.isArray(data.payload) ? data.payload : {}
      const payloadText = JSON.stringify(payload)

      if (!clientEventId || clientEventId.length > 80) return error('ID do evento inválido.', 400)
      if (!TYPE_RE.test(eventType)) return error('Tipo de evento inválido.', 400)
      if (tripId !== null && !UUID_RE.test(tripId)) return error('Viagem inválida.', 400)
      if (payloadText.length > 16000) return error('Payload do evento excede o limite.', 413)

      const occurred = data.occurredAtUtc ?? data.createdAtUtc
      const occurredAt = occurred ? new Date(String(occurred)) : new Date()
      if (Number.isNaN(occurredAt.getTime())) return error('Data do evento inválida.', 400)

      const sql = neon(c.env.DATABASE_URL!)
      if (tripId) {
        const ownsTrip = await sql`SELECT id FROM trips WHERE id=${tripId} AND user_id=${user.id} LIMIT 1`
        if (!ownsTrip[0]) return error('Viagem não encontrada.', 404)
      }

      const rows = await sql`
        INSERT INTO transpoli_operational_events(user_id, trip_id, client_event_id, event_type, occurred_at, payload)
        VALUES(${user.id}, ${tripId}, ${clientEventId}, ${eventType}, ${occurredAt.toISOString()}, ${payloadText}::jsonb)
        ON CONFLICT(user_id, client_event_id) DO UPDATE SET client_event_id=EXCLUDED.client_event_id
        RETURNING id, user_id, trip_id, client_event_id, event_type, occurred_at, created_at
      `
      return c.json({ ok: true, event: rows[0], duplicateSafe: true }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (e) {
      console.error('transpoli_event_ingest_error', e)
      return error('Erro ao registrar evento.', 500)
    }
  })

  app.get('/me/events', async (c: any) => {
    try {
      const user = await currentUser(c)
      if (!user) return error('Sessão inválida ou expirada.', 401)
      const q = c.req.query()
      const type = String(q.type ?? '').trim().toLowerCase()
      const tripId = String(q.tripId ?? '').trim()
      const limit = Math.min(200, Math.max(1, Number.parseInt(q.limit ?? '50', 10) || 50))
      const from = q.from ? new Date(String(q.from)) : null
      const to = q.to ? new Date(String(q.to)) : null
      if (type && !TYPE_RE.test(type)) return error('Tipo de evento inválido.', 400)
      if (tripId && !UUID_RE.test(tripId)) return error('Viagem inválida.', 400)
      if (from && Number.isNaN(from.getTime())) return error('Data inicial inválida.', 400)
      if (to && Number.isNaN(to.getTime())) return error('Data final inválida.', 400)

      const sql = neon(c.env.DATABASE_URL!)
      const rows = await sql`
        SELECT id, trip_id, client_event_id, event_type, occurred_at, payload, created_at
        FROM transpoli_operational_events
        WHERE user_id=${user.id}
          AND (${type}='' OR event_type=${type})
          AND (${tripId}='' OR trip_id=${tripId})
          AND (${from ? from.toISOString() : null}::timestamptz IS NULL OR occurred_at >= ${from ? from.toISOString() : null})
          AND (${to ? to.toISOString() : null}::timestamptz IS NULL OR occurred_at <= ${to ? to.toISOString() : null})
        ORDER BY occurred_at DESC
        LIMIT ${limit}
      `
      return c.json({ ok: true, events: rows }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (e) {
      console.error('transpoli_event_list_error', e)
      return error('Erro ao carregar eventos.', 500)
    }
  })
}
