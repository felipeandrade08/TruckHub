import { neon } from '@neondatabase/serverless'

const SESSION_COOKIE = 'truckhub_session'

function getCookie(request: Request, name: string) {
  const header = request.headers.get('Cookie') ?? ''
  for (const part of header.split(';')) {
    const [key, ...value] = part.trim().split('=')
    if (key === name) {
      const raw = value.join('=')
      try { return decodeURIComponent(raw) } catch { return raw }
    }
  }
  return null
}

async function hashSessionToken(token: string) {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(token))
  let binary = ''
  for (const byte of new Uint8Array(digest)) binary += String.fromCharCode(byte)
  return btoa(binary)
}

export function registerSessionHardening(app: any) {
  app.use('/me/*', async (c: any, next: any) => {
    if (!c.env.DATABASE_URL) return next()

    try {
      const token = getCookie(c.req.raw, SESSION_COOKIE)
      if (token) {
        const tokenHash = await hashSessionToken(token)
        const sql = neon(c.env.DATABASE_URL)
        await sql`
          UPDATE sessions
          SET last_seen_at = NOW()
          WHERE token_hash = ${tokenHash}
            AND session_type = 'web'
            AND revoked_at IS NULL
            AND expires_at > NOW()
            AND (last_seen_at IS NULL OR last_seen_at < NOW() - INTERVAL '15 minutes')
        `

        await sql`
          DELETE FROM sessions
          WHERE expires_at < NOW() - INTERVAL '30 days'
             OR revoked_at < NOW() - INTERVAL '30 days'
        `
      }
    } catch (error) {
      console.error('session_hardening_error', error)
    }

    return next()
  })
}
