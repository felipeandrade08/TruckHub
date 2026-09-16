import process from 'node:process'
import { neon } from '@neondatabase/serverless'

const databaseUrl = process.env.DATABASE_URL
if (!databaseUrl) {
  console.error('DATABASE_URL não configurada.')
  process.exit(1)
}

const sql = neon(databaseUrl)
const run = await sql`INSERT INTO financial_reconciliation_runs(status) VALUES ('running') RETURNING id`
const runId = run[0].id

console.log('TruckHub — reconciliação financeira')
console.log(`run=${runId}`)

const candidates = await sql`
  SELECT p.id,p.user_id
  FROM payments p
  JOIN users u ON u.id=p.user_id AND u.status='active'
  LEFT JOIN licenses l ON l.user_id=p.user_id
  WHERE p.provider='mercado_pago'
    AND p.status='approved'
    AND p.external_id IS NOT NULL
    AND p.external_reference IS NOT NULL
    AND p.amount > 0
    AND p.currency='BRL'
    AND (l.id IS NULL OR l.license_type <> 'lifetime' OR l.status <> 'active' OR l.expires_at IS NOT NULL)
  ORDER BY p.created_at ASC
  LIMIT 100
`

let repaired = 0
let failed = 0
for (const candidate of candidates) {
  try {
    const rows = await sql`SELECT * FROM truckhub_reconcile_approved_payment(${candidate.id})`
    if (rows[0]?.repaired) {
      repaired++
      await sql`INSERT INTO financial_reconciliation_items(run_id,payment_id,user_id,action) VALUES(${runId},${candidate.id},${candidate.user_id},'repaired')`
      console.log(`REPAIRED payment=${candidate.id} user=${candidate.user_id}`)
    } else {
      await sql`INSERT INTO financial_reconciliation_items(run_id,payment_id,user_id,action) VALUES(${runId},${candidate.id},${candidate.user_id},'already_consistent')`
    }
  } catch (error) {
    failed++
    const detail = error instanceof Error ? error.message : String(error)
    await sql`INSERT INTO financial_reconciliation_items(run_id,payment_id,user_id,action,error_detail) VALUES(${runId},${candidate.id},${candidate.user_id},'failed',${detail.slice(0,500)})`
    console.error(`FAIL payment=${candidate.id}`, detail)
  }
}

const status = failed > 0 ? 'failed' : 'completed'
await sql`UPDATE financial_reconciliation_runs SET finished_at=NOW(),status=${status},scanned_count=${candidates.length},repaired_count=${repaired},failed_count=${failed} WHERE id=${runId}`

console.log(`Candidatos: ${candidates.length}`)
console.log(`Corrigidos: ${repaired}`)
console.log(`Falhas: ${failed}`)
if (failed > 0) process.exit(2)
