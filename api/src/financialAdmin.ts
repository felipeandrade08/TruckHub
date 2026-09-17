import { neon } from '@neondatabase/serverless'

interface Env {
  DATABASE_URL?: string
  TRUCKHUB_ADMIN_SECRET?: string
}

function unauthorized() {
  return new Response(JSON.stringify({ ok: false, error: 'Acesso administrativo não autorizado.' }), {
    status: 401,
    headers: { 'content-type': 'application/json; charset=UTF-8', 'cache-control': 'no-store' }
  })
}

function safeLimit(value: string | undefined, fallback = 50) {
  const n = Number(value)
  return Number.isInteger(n) && n >= 1 && n <= 100 ? n : fallback
}

async function requireAdmin(c: any) {
  const configured = String(c.env.TRUCKHUB_ADMIN_SECRET ?? '')
  const supplied = c.req.header('Authorization')?.replace(/^Bearer\s+/i, '').trim() ?? ''
  if (!configured || !supplied) return false
  const a = new TextEncoder().encode(configured)
  const b = new TextEncoder().encode(supplied)
  if (a.length !== b.length) return false
  let diff = 0
  for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i]
  return diff === 0
}

export function registerFinancialAdminRoutes(app: any) {
  app.get('/internal/admin/finance/overview', async (c: any) => {
    if (!(await requireAdmin(c))) return unauthorized()
    if (!c.env.DATABASE_URL) return new Response(JSON.stringify({ ok: false, error: 'Banco não configurado.' }), { status: 500 })
    const sql = neon(c.env.DATABASE_URL)
    const [summary, problems, runs] = await Promise.all([
      sql`
        SELECT
          COUNT(*) FILTER (WHERE status='approved')::int AS approved_count,
          COALESCE(SUM(amount) FILTER (WHERE status='approved'),0)::numeric AS approved_amount,
          COUNT(*) FILTER (WHERE status IN ('pending','in_process','review'))::int AS pending_count,
          COUNT(*) FILTER (WHERE status IN ('failed','rejected','cancelled'))::int AS failed_count,
          COUNT(*) FILTER (WHERE status='approved' AND (paid_at IS NULL OR external_id IS NULL OR external_reference IS NULL))::int AS inconsistent_count
        FROM payments
        WHERE provider='mercado_pago'
      `,
      sql`
        SELECT p.id,p.user_id,p.status,p.amount,p.currency,p.external_id,p.external_reference,p.preference_id,p.paid_at,p.created_at,
               CASE
                 WHEN p.status='approved' AND (p.external_id IS NULL OR p.external_reference IS NULL) THEN 'approved_payment_identity_incomplete'
                 WHEN p.status='approved' AND (l.id IS NULL OR l.license_type <> 'lifetime' OR l.status <> 'active' OR l.expires_at IS NOT NULL) THEN 'approved_payment_without_lifetime_license'
                 ELSE 'review'
               END AS problem
        FROM payments p
        LEFT JOIN licenses l ON l.user_id=p.user_id
        WHERE p.provider='mercado_pago'
          AND (
            (p.status='approved' AND (p.external_id IS NULL OR p.external_reference IS NULL))
            OR (p.status='approved' AND (l.id IS NULL OR l.license_type <> 'lifetime' OR l.status <> 'active' OR l.expires_at IS NOT NULL))
          )
        ORDER BY p.created_at DESC
        LIMIT 100
      `,
      sql`
        SELECT id,started_at,finished_at,status,scanned_count,repaired_count,failed_count,error_detail
        FROM financial_reconciliation_runs
        ORDER BY started_at DESC
        LIMIT 20
      `
    ])
    return c.json({ ok: true, summary: summary[0], problems, reconciliationRuns: runs }, { headers: { 'Cache-Control': 'no-store' } })
  })

  app.get('/internal/admin/finance/payments', async (c: any) => {
    if (!(await requireAdmin(c))) return unauthorized()
    if (!c.env.DATABASE_URL) return new Response(JSON.stringify({ ok: false, error: 'Banco não configurado.' }), { status: 500 })
    const sql = neon(c.env.DATABASE_URL)
    const limit = safeLimit(c.req.query('limit'))
    const rows = await sql`
      SELECT p.id,p.user_id,p.provider,p.status,p.provider_status,p.status_detail,p.amount,p.currency,
             p.preference_id,p.external_id,p.external_reference,p.paid_at,p.created_at,p.updated_at,
             l.license_type,l.status AS license_status,l.expires_at
      FROM payments p
      LEFT JOIN licenses l ON l.user_id=p.user_id
      WHERE p.provider='mercado_pago'
      ORDER BY p.created_at DESC
      LIMIT ${limit}
    `
    return c.json({ ok: true, payments: rows }, { headers: { 'Cache-Control': 'no-store' } })
  })

  app.get('/internal/admin/finance/webhooks', async (c: any) => {
    if (!(await requireAdmin(c))) return unauthorized()
    if (!c.env.DATABASE_URL) return new Response(JSON.stringify({ ok: false, error: 'Banco não configurado.' }), { status: 500 })
    const sql = neon(c.env.DATABASE_URL)
    const limit = safeLimit(c.req.query('limit'))
    const rows = await sql`
      SELECT id,event_id,payment_id,event_type,action,result,error_detail,received_at,processed_at
      FROM mercado_pago_webhook_events
      ORDER BY received_at DESC
      LIMIT ${limit}
    `
    return c.json({ ok: true, webhooks: rows }, { headers: { 'Cache-Control': 'no-store' } })
  })

  app.get('/internal/admin/finance/reconciliation', async (c: any) => {
    if (!(await requireAdmin(c))) return unauthorized()
    if (!c.env.DATABASE_URL) return new Response(JSON.stringify({ ok: false, error: 'Banco não configurado.' }), { status: 500 })
    const sql = neon(c.env.DATABASE_URL)
    const rows = await sql`
      SELECT id,started_at,finished_at,status,scanned_count,repaired_count,failed_count,error_detail
      FROM financial_reconciliation_runs
      ORDER BY started_at DESC
      LIMIT 50
    `
    const items = await sql`
      SELECT i.id,i.run_id,i.payment_id,i.user_id,i.action,i.error_detail,i.created_at
      FROM financial_reconciliation_items i
      ORDER BY i.created_at DESC
      LIMIT 100
    `
    return c.json({ ok: true, runs: rows, items }, { headers: { 'Cache-Control': 'no-store' } })
  })
}
