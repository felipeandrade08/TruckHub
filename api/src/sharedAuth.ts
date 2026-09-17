import { neon } from '@neondatabase/serverless'

export const SESSION_COOKIE = 'truckhub_session'

function toBase64(bytes: Uint8Array) {
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary)
}

export async function hashSessionToken(token: string) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(token))
  return toBase64(new Uint8Array(digest))
}

export function getCookie(request: Request, name: string) {
  const header = request.headers.get('Cookie') ?? ''
  for (const part of header.split(';')) {
    const [key, ...value] = part.trim().split('=')
    if (key === name) return value.join('=')
  }
  return null
}

export async function requireUser(c: any) {
  if (!c.env.DATABASE_URL) return null
  const token = getCookie(c.req.raw, SESSION_COOKIE)
  if (!token) return null
  const tokenHash = await hashSessionToken(token)
  const sql = neon(c.env.DATABASE_URL)
  const rows = await sql`
    SELECT u.id, u.name, u.email, u.created_at
    FROM sessions s
    JOIN users u ON u.id = s.user_id
    WHERE s.token_hash = ${tokenHash}
      AND s.revoked_at IS NULL
      AND s.expires_at > NOW()
      AND u.status = 'active'
    LIMIT 1
  `
  return rows[0] ?? null
}

export function jsonError(message: string, status: 400 | 401 | 404 | 409 | 500) {
  return new Response(JSON.stringify({ ok: false, error: message }), {
    status,
    headers: { 'content-type': 'application/json; charset=UTF-8' }
  })
}
