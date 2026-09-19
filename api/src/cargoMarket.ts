import { neon } from '@neondatabase/serverless'
import { hashSessionToken, getCookie } from './sharedAuth'

const RATE_MIN = 5
const RATE_MAX = 12
const RATE_INTERVAL_MS = 20 * 60 * 1000

function normalize(value: any) { return String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim() }
function slug(value: string) { return normalize(value).replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 70) || 'carga_geral' }
function hash(key: string) { let h = 0; for (const ch of key) h = (h * 31 + ch.charCodeAt(0)) % 100000; return h }
function dynamicRate(base: number, key: string, now = Date.now()) {
  const slot = Math.floor(now / RATE_INTERVAL_MS)
  const wave = (slot + hash(key)) % 11
  return Number(Math.min(RATE_MAX, Math.max(RATE_MIN, base + wave * 0.55 - 2.75)).toFixed(2))
}
function statusFor(rate: number) { if (rate >= 9) return 'high'; if (rate <= 6.5) return 'low'; return 'normal' }
function text(value: any, max: number) { const s = String(value ?? '').trim(); return s ? s.slice(0, max) : null }

async function user(c: any) {
  if (!c.env.DATABASE_URL) return null
  const bearer = c.req.header('Authorization')?.replace(/^Bearer\s+/i, '').trim()
  const token = bearer || getCookie(c.req.raw, 'truckhub_session')
  if (!token) return null
  const sql = neon(c.env.DATABASE_URL)
  const tokenHash = await hashSessionToken(token)
  const rows = await sql`SELECT u.id,u.name,u.email FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${tokenHash} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND u.status='active' LIMIT 1`
  return rows[0] ?? null
}
function unauthorized(c: any) { return c.json({ ok:false, error:'Sessão inválida ou expirada.' },401) }

export async function ensureCargo(sql: any, cargoName: string) {
  const displayName = cargoName.trim().slice(0,180)
  const key = slug(displayName)
  const existing = await sql`SELECT id,cargo_key,display_name,rate_brl_km,market_status,active,discovered_count FROM cargo_market_offers WHERE cargo_key=${key} LIMIT 1`
  if (existing[0]) {
    await sql`UPDATE cargo_market_offers SET discovered_count=discovered_count+1,last_discovered_at=NOW(),updated_at=NOW(),active=TRUE WHERE id=${existing[0].id}`
    return existing[0]
  }
  const rate = Number((RATE_MIN + (hash(key) % 15) * 0.5).toFixed(2))
  const marketStatus = statusFor(rate)
  await sql`INSERT INTO cargo_market_offers(cargo_key,display_name,rate_brl_km,market_status,discovered_count,last_discovered_at) VALUES(${key},${displayName},${rate},${marketStatus},1,NOW()) ON CONFLICT(cargo_key) DO NOTHING`
  await sql`INSERT INTO cargo_rates(cargo_key,display_name,rate_brl_km,active) VALUES(${key},${displayName},${rate},TRUE) ON CONFLICT(cargo_key) DO UPDATE SET display_name=EXCLUDED.display_name,active=TRUE`
  const rows = await sql`SELECT id,cargo_key,display_name,rate_brl_km,market_status,active,discovered_count FROM cargo_market_offers WHERE cargo_key=${key} LIMIT 1`
  return rows[0]
}

export function registerCargoMarketRoutes(app:any) {
  app.get('/me/cargo-market', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const sql=neon(c.env.DATABASE_URL)
      const [rows, popularCargo, activeDriver, trailerUsage] = await Promise.all([
        sql`SELECT id,cargo_key,display_name,rate_brl_km,market_status,active,discovered_count,last_discovered_at FROM cargo_market_offers WHERE active=TRUE ORDER BY rate_brl_km DESC,display_name ASC`,
        sql`SELECT cargo,COUNT(*)::int AS trip_count FROM trips WHERE status='finished' AND cargo IS NOT NULL AND BTRIM(cargo)<>'' GROUP BY cargo ORDER BY trip_count DESC,cargo ASC LIMIT 1`,
        sql`SELECT u.name,COUNT(t.id)::int AS trip_count FROM trips t JOIN users u ON u.id=t.user_id WHERE t.status='finished' GROUP BY u.id,u.name ORDER BY trip_count DESC,u.name ASC LIMIT 1`,
        sql`SELECT COALESCE(NULLIF(payload->>'trailer_name',''),NULLIF(payload->>'trailerName',''),NULLIF(payload->>'trailer','')) AS trailer,COUNT(*)::int AS usage_count FROM trip_events WHERE payload IS NOT NULL AND (payload ? 'trailer_name' OR payload ? 'trailerName' OR payload ? 'trailer') GROUP BY trailer ORDER BY usage_count DESC,trailer ASC LIMIT 1`
      ])
      const offers=rows.map((row:any)=>{const rate=dynamicRate(Number(row.rate_brl_km)||4,String(row.cargo_key));return {...row,base_rate_brl_km:Number(row.rate_brl_km)||4,rate_brl_km:rate,market_status:statusFor(rate)}})
      return c.json({ok:true,policy:{minimumBrlKm:RATE_MIN,maximumBrlKm:RATE_MAX,changeIntervalMinutes:20},offers,dashboard:{popularCargo:popularCargo[0]??null,activeDriver:activeDriver[0]??null,trailerUsage:trailerUsage[0]??null}},{headers:{'Cache-Control':'no-store'}})
    } catch(e) { console.error('cargo_market_load_error',e); return c.json({ok:false,error:'Erro ao carregar o mercado de cargas.'},500) }
  })

  app.post('/me/cargo-market/discover', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const body=await c.req.json().catch(()=>null) as any
      const cargo=text(body?.cargo,180)
      if(!cargo)return c.json({ok:false,error:'Informe a carga detectada.'},400)
      const sql=neon(c.env.DATABASE_URL)
      const offer=await ensureCargo(sql,cargo)
      return c.json({ok:true,discovered:true,offer},{headers:{'Cache-Control':'no-store'}})
    } catch(e) { console.error('cargo_discover_error',e); return c.json({ok:false,error:'Não foi possível adicionar a carga ao mercado.'},500) }
  })

}
