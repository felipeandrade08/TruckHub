import fs from 'node:fs/promises'
import path from 'node:path'
import process from 'node:process'
import { fileURLToPath } from 'node:url'
import { neon } from '@neondatabase/serverless'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const migrationsDir = path.resolve(__dirname, '../../database/migrations')
const databaseUrl = process.env.DATABASE_URL

if (!databaseUrl) {
  console.error('DATABASE_URL não configurada.')
  process.exit(1)
}

const sql = neon(databaseUrl)
const failures = []
const warnings = []

async function check(name, query, options = {}) {
  const rows = await query
  const count = Number(rows[0]?.count ?? 0)
  const kind = options.warning ? 'WARN' : count > 0 ? 'FAIL' : 'OK  '
  console.log(`${kind} ${name}: ${count}`)
  if (count > 0) (options.warning ? warnings : failures).push(`${name}: ${count}`)
}

console.log('TruckHub — auditoria somente leitura')
console.log('Nenhum dado será alterado.')
console.log('')

const migrationFiles = (await fs.readdir(migrationsDir))
  .filter(name => /^\d+_.+\.sql$/i.test(name))
  .sort((a, b) => a.localeCompare(b, 'en'))

const migrationTable = await sql`SELECT to_regclass('public.truckhub_schema_migrations') AS name`
if (!migrationTable[0]?.name) {
  failures.push('truckhub_schema_migrations: tabela ausente')
  console.log('FAIL truckhub_schema_migrations: tabela ausente')
} else {
  const appliedRows = await sql`SELECT version FROM truckhub_schema_migrations ORDER BY version`
  const applied = new Set(appliedRows.map(row => row.version))
  const pending = migrationFiles.filter(name => !applied.has(name))
  const unknown = appliedRows.filter(row => !migrationFiles.includes(row.version)).map(row => row.version)
  console.log(`OK   migrações aplicadas: ${appliedRows.length}`)
  console.log(`OK   migrações no repositório: ${migrationFiles.length}`)
  if (pending.length) { warnings.push(`migrações pendentes: ${pending.join(', ')}`); console.log(`WARN migrações pendentes: ${pending.join(', ')}`) }
  if (unknown.length) { warnings.push(`migrações registradas sem arquivo: ${unknown.join(', ')}`); console.log(`WARN migrações registradas sem arquivo: ${unknown.join(', ')}`) }
}

console.log('')
console.log('Integridade de identidade/licença')
await check('usuários sem licença', sql`SELECT COUNT(*)::bigint AS count FROM users u LEFT JOIN licenses l ON l.user_id = u.id WHERE l.id IS NULL`)
await check('licenças sem usuário', sql`SELECT COUNT(*)::bigint AS count FROM licenses l LEFT JOIN users u ON u.id = l.user_id WHERE u.id IS NULL`)
await check('mais de uma licença por usuário', sql`SELECT COUNT(*)::bigint AS count FROM (SELECT user_id FROM licenses GROUP BY user_id HAVING COUNT(*) > 1) x`)
await check('lifetime com expires_at', sql`SELECT COUNT(*)::bigint AS count FROM licenses WHERE license_type = 'lifetime' AND expires_at IS NOT NULL`)
await check('trial sem trial_expires_at', sql`SELECT COUNT(*)::bigint AS count FROM licenses WHERE license_type = 'trial' AND trial_expires_at IS NULL`)

console.log('')
console.log('Integridade de dispositivos/sessões')
await check('dispositivo sem licença', sql`SELECT COUNT(*)::bigint AS count FROM devices d LEFT JOIN licenses l ON l.id = d.license_id WHERE l.id IS NULL`)
await check('mais de um dispositivo por licença', sql`SELECT COUNT(*)::bigint AS count FROM (SELECT license_id FROM devices GROUP BY license_id HAVING COUNT(*) > 1) x`)
await check('dispositivo ativo duplicado', sql`SELECT COUNT(*)::bigint AS count FROM (SELECT device_id FROM devices WHERE status = 'active' GROUP BY device_id HAVING COUNT(*) > 1) x`)
await check('sessão desktop ativa sem dispositivo', sql`SELECT COUNT(*)::bigint AS count FROM sessions WHERE session_type = 'desktop' AND revoked_at IS NULL AND device_id IS NULL`)
await check('mais de uma sessão desktop ativa por usuário', sql`SELECT COUNT(*)::bigint AS count FROM (SELECT user_id FROM sessions WHERE session_type = 'desktop' AND revoked_at IS NULL AND expires_at > NOW() GROUP BY user_id HAVING COUNT(*) > 1) x`)
await check('sessões ativas expiradas', sql`SELECT COUNT(*)::bigint AS count FROM sessions WHERE revoked_at IS NULL AND expires_at <= NOW()`, { warning: true })

console.log('')
console.log('Integridade de viagens/telemetria')
await check('mais de uma viagem ativa por usuário', sql`SELECT COUNT(*)::bigint AS count FROM (SELECT user_id FROM trips WHERE status = 'active' GROUP BY user_id HAVING COUNT(*) > 1) x`)
await check('viagem finished sem finished_at', sql`SELECT COUNT(*)::bigint AS count FROM trips WHERE status = 'finished' AND finished_at IS NULL`)
await check('viagem ativa com finished_at', sql`SELECT COUNT(*)::bigint AS count FROM trips WHERE status = 'active' AND finished_at IS NOT NULL`)
await check('distância negativa', sql`SELECT COUNT(*)::bigint AS count FROM trips WHERE distance_km < 0`)
await check('combustível negativo', sql`SELECT COUNT(*)::bigint AS count FROM trips WHERE fuel_used_l < 0`)
await check('telemetria com velocidade inválida', sql`SELECT COUNT(*)::bigint AS count FROM trip_telemetry_samples WHERE speed_kph < 0 OR speed_kph > 250`)
await check('telemetria com RPM inválido', sql`SELECT COUNT(*)::bigint AS count FROM trip_telemetry_samples WHERE rpm < 0 OR rpm > 10000`)
await check('telemetria com combustível inválido', sql`SELECT COUNT(*)::bigint AS count FROM trip_telemetry_samples WHERE fuel_l < 0 OR fuel_l > 2000`)

console.log('')
console.log('Integridade financeira / Mercado Pago')
await check('pagamentos com valor inválido', sql`SELECT COUNT(*)::bigint AS count FROM payments WHERE amount <= 0 OR amount > 100000`)
await check('pagamentos fora de BRL', sql`SELECT COUNT(*)::bigint AS count FROM payments WHERE currency <> 'BRL'`, { warning: true })
await check('pagamentos fora do provedor permitido', sql`SELECT COUNT(*)::bigint AS count FROM payments WHERE provider <> 'mercado_pago'`)
await check('pagamentos duplicados por provider/external_id', sql`SELECT COUNT(*)::bigint AS count FROM (SELECT provider, external_id FROM payments WHERE external_id IS NOT NULL GROUP BY provider, external_id HAVING COUNT(*) > 1) x`)
await check('pagamentos aprovados sem paid_at', sql`SELECT COUNT(*)::bigint AS count FROM payments WHERE status = 'approved' AND paid_at IS NULL`)
await check('pagamentos aprovados sem licença vitalícia ativa', sql`
  SELECT COUNT(*)::bigint AS count
  FROM payments p
  JOIN users u ON u.id = p.user_id AND u.status = 'active'
  LEFT JOIN licenses l ON l.user_id = p.user_id
  WHERE p.provider = 'mercado_pago'
    AND p.status = 'approved'
    AND p.external_id IS NOT NULL
    AND p.external_reference IS NOT NULL
    AND p.amount > 0
    AND p.currency = 'BRL'
    AND (l.id IS NULL OR l.license_type <> 'lifetime' OR l.status <> 'active' OR l.expires_at IS NOT NULL)
`)
await check('webhooks processados sem resultado', sql`SELECT COUNT(*)::bigint AS count FROM mercado_pago_webhook_events WHERE processed_at IS NOT NULL AND (result IS NULL OR result = '')`, { warning: true })
await check('pagamentos aprovados com identidade incompleta', sql`SELECT COUNT(*)::bigint AS count FROM payments WHERE provider = 'mercado_pago' AND status = 'approved' AND (external_id IS NULL OR external_reference IS NULL OR external_reference = '')`)

console.log('')
if (failures.length) {
  console.error('AUDITORIA REPROVADA:')
  for (const item of failures) console.error(` - ${item}`)
  if (warnings.length) { console.error('Avisos:'); for (const item of warnings) console.error(` - ${item}`) }
  process.exit(2)
}

console.log('AUDITORIA OK: nenhuma inconsistência crítica encontrada.')
if (warnings.length) { console.log('Avisos não bloqueantes:'); for (const item of warnings) console.log(` - ${item}`) }
