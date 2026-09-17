import { neon } from '@neondatabase/serverless'

const DEFAULT_WINDOW_SECONDS = 60
const DEFAULT_MAX_ATTEMPTS = 10

export type RateLimitOptions = {
  maxAttempts?: number
  windowSeconds?: number
  includeBodyIdentity?: boolean
}

function getClientIp(request: Request) {
  return request.headers.get('CF-Connecting-IP')?.trim() || 'unknown'
}

function normalizeIdentity(value: unknown) {
  return typeof value === 'string' ? value.trim().toLowerCase().slice(0, 320) : ''
}

async function digestKey(value: string, pepper: string) {
  const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(`${pepper}:${value}`))
  let binary = ''
  for (const byte of new Uint8Array(bytes)) binary += String.fromCharCode(byte)
  return btoa(binary)
}

async function readBodyIdentity(request: Request) {
  try {
    const data = await request.clone().json() as { email?: unknown; deviceId?: unknown }
    const email = normalizeIdentity(data?.email)
    const deviceId = typeof data?.deviceId === 'string' ? data.deviceId.trim().slice(0, 255) : ''
    return [email ? `email:${email}` : '', deviceId ? `device:${deviceId}` : ''].filter(Boolean)
  } catch {
    return []
  }
}

export async function checkRateLimit(c: any, scope: string, options: RateLimitOptions = {}) {
  if (!c.env.DATABASE_URL) return null

  const maxAttempts = Math.max(1, Math.min(options.maxAttempts ?? DEFAULT_MAX_ATTEMPTS, 100))
  const windowSeconds = Math.max(1, Math.min(options.windowSeconds ?? DEFAULT_WINDOW_SECONDS, 3600))
  const pepper = typeof c.env.RATE_LIMIT_PEPPER === 'string' ? c.env.RATE_LIMIT_PEPPER : ''
  if (pepper.length < 16) {
    console.error('rate_limit_pepper_missing')
    return new Response(JSON.stringify({ ok: false, error: 'Serviço temporariamente indisponível.', code: 'RATE_LIMIT_CONFIG_ERROR' }), {
      status: 503,
      headers: { 'content-type': 'application/json; charset=UTF-8', 'cache-control': 'no-store', 'retry-after': '60' }
    })
  }

  const identities = [`ip:${getClientIp(c.req.raw)}`]
  if (options.includeBodyIdentity) identities.push(...await readBodyIdentity(c.req.raw))

  const sql = neon(c.env.DATABASE_URL)
  const now = new Date()
  const windowStart = new Date(now.getTime() - windowSeconds * 1000)
  let retryAfter = 0

  for (const identity of identities) {
    const keyHash = await digestKey(`v1:${scope}:${identity}`, pepper)
    const rows = await sql`
      INSERT INTO security_rate_limits (key_hash, window_started_at, attempts, updated_at)
      VALUES (${keyHash}, ${now.toISOString()}, 1, ${now.toISOString()})
      ON CONFLICT (key_hash) DO UPDATE
        SET attempts = CASE
          WHEN security_rate_limits.window_started_at <= ${windowStart.toISOString()} THEN 1
          ELSE security_rate_limits.attempts + 1
        END,
        window_started_at = CASE
          WHEN security_rate_limits.window_started_at <= ${windowStart.toISOString()} THEN ${now.toISOString()}
          ELSE security_rate_limits.window_started_at
        END,
        updated_at = ${now.toISOString()}
      RETURNING attempts, window_started_at
    `
    const row = rows[0]
    if (!row) continue
    if (Number(row.attempts) > maxAttempts) {
      const started = new Date(row.window_started_at).getTime()
      retryAfter = Math.max(retryAfter, Math.ceil((started + windowSeconds * 1000 - now.getTime()) / 1000))
    }
  }

  try { await sql`DELETE FROM security_rate_limits WHERE updated_at < NOW() - INTERVAL '2 hours'` } catch { }

  if (retryAfter > 0) {
    return new Response(JSON.stringify({ ok: false, error: 'Muitas tentativas. Aguarde alguns segundos e tente novamente.', code: 'RATE_LIMITED' }), {
      status: 429,
      headers: { 'content-type': 'application/json; charset=UTF-8', 'cache-control': 'no-store', 'retry-after': String(retryAfter) }
    })
  }
  return null
}
