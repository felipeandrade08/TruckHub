import { neon } from '@neondatabase/serverless'
import { hashSessionToken, getCookie } from './sharedAuth'

const RATE_MIN = 5
const RATE_MAX = 12
const RATE_INTERVAL_MS = 20 * 60 * 1000
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

function normalize(value: any) { return String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim() }
function slug(value: string) { return normalize(value).replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 70) || 'carga_geral' }
function hash(key: string) { let h = 0; for (const ch of key) h = (h * 31 + ch.charCodeAt(0)) % 100000; return h }
function dynamicRate(base: number, key: string, now = Date.now()) {
  const slot = Math.floor(now / RATE_INTERVAL_MS)
  const wave = (slot + hash(key)) % 11
  return Number(Math.min(RATE_MAX, Math.max(RATE_MIN, base + wave * 0.55 - 2.75)).toFixed(2))
}
function statusFor(rate: number) { if (rate >= 5.2) return 'high'; if (rate <= 4.4) return 'low'; return 'normal' }
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

async function ensureCargo(sql: any, cargoName: string) {
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

  app.get('/me/cargo-market/contracts', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const sql=neon(c.env.DATABASE_URL)
      const rows=await sql`SELECT c.id,c.offer_id,c.cargo_key,c.cargo,c.rate_brl_km,c.status,c.origin,c.destination,c.distance_km,c.cargo_mass_kg,c.bonus_brl,c.damage_penalty_pct,c.clean_delivery_bonus_pct,c.accepted_at,c.started_at,c.delivered_at,c.trip_id FROM cargo_contracts c WHERE c.user_id=${u.id} ORDER BY c.accepted_at DESC LIMIT 100`
      return c.json({ok:true,contracts:rows})
    } catch(e) { console.error('cargo_contracts_error',e); return c.json({ok:false,error:'Erro ao carregar contratos.'},500) }
  })

  app.post('/me/cargo-market/contracts', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const body=await c.req.json().catch(()=>null) as any
      const cargo=text(body?.cargo,180)
      if(!cargo)return c.json({ok:false,error:'Carga inválida.'},400)
      const sql=neon(c.env.DATABASE_URL)
      const offer=body?.offerId && UUID_RE.test(String(body.offerId)) ? (await sql`SELECT * FROM cargo_market_offers WHERE id=${body.offerId} AND active=TRUE LIMIT 1`)[0] : await ensureCargo(sql,cargo)
      if(!offer)return c.json({ok:false,error:'Oferta de carga não encontrada.'},404)
      const rateFromMarket=Number(body?.marketRateBrlKm)
      const currentRate=dynamicRate(Number(offer.rate_brl_km)||4,String(offer.cargo_key))
      const rate=Number.isFinite(rateFromMarket)&&rateFromMarket>=RATE_MIN&&rateFromMarket<=RATE_MAX?Number(rateFromMarket.toFixed(2)):currentRate
      const active=await sql`SELECT id FROM cargo_contracts WHERE user_id=${u.id} AND status IN ('accepted','active') LIMIT 1`
      if(active[0])return c.json({ok:false,error:'Você já possui um contrato de carga em andamento.'},409)
      const bonus=Math.max(0,Number(body?.bonusBrl)||0)
      const rows=await sql`INSERT INTO cargo_contracts(user_id,offer_id,cargo_key,cargo,rate_brl_km,bonus_brl,status) VALUES(${u.id},${offer.id},${offer.cargo_key},${cargo},${rate},${bonus},'accepted') RETURNING id,offer_id,cargo_key,cargo,rate_brl_km,status,origin,destination,distance_km,cargo_mass_kg,bonus_brl,damage_penalty_pct,clean_delivery_bonus_pct,accepted_at,started_at,delivered_at,trip_id`
      return c.json({ok:true,contract:rows[0]},201)
    } catch(e) { console.error('cargo_contract_create_error',e); return c.json({ok:false,error:'Erro ao aceitar contrato.'},500) }
  })

  app.post('/me/cargo-market/contracts/:id/link-trip', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const id=c.req.param('id'); if(!UUID_RE.test(id))return c.json({ok:false,error:'Contrato não encontrado.'},404)
      const body=await c.req.json().catch(()=>null) as any; const tripId=String(body?.tripId||'')
      if(!UUID_RE.test(tripId))return c.json({ok:false,error:'Viagem inválida.'},400)
      const sql=neon(c.env.DATABASE_URL)
      const trip=(await sql`SELECT id,cargo,origin,destination,distance_km,cargo_mass_kg FROM trips WHERE id=${tripId} AND user_id=${u.id} LIMIT 1`)[0]
      if(!trip)return c.json({ok:false,error:'Viagem não encontrada.'},404)
      const rows=await sql`UPDATE cargo_contracts SET trip_id=${tripId},status='active',started_at=COALESCE(started_at,NOW()),origin=COALESCE(${trip.origin},origin),destination=COALESCE(${trip.destination},destination),distance_km=COALESCE(${trip.distance_km},distance_km),cargo_mass_kg=COALESCE(${trip.cargo_mass_kg},cargo_mass_kg) WHERE id=${id} AND user_id=${u.id} AND status='accepted' RETURNING *`
      if(!rows[0])return c.json({ok:false,error:'Contrato não está disponível para vinculação.'},409)
      await sql`UPDATE trips SET cargo_contract_id=${id},cargo_value_brl=COALESCE(cargo_value_brl,0) WHERE id=${tripId} AND user_id=${u.id}`
      return c.json({ok:true,contract:rows[0]})
    } catch(e) { console.error('cargo_contract_link_error',e); return c.json({ok:false,error:'Erro ao vincular contrato à viagem.'},500) }
  })

  app.post('/me/cargo-market/contracts/:id/deliver', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const id=c.req.param('id'); if(!UUID_RE.test(id))return c.json({ok:false,error:'Contrato não encontrado.'},404)
      const sql=neon(c.env.DATABASE_URL)
      const rows=await sql`UPDATE cargo_contracts SET status='delivered',delivered_at=NOW() WHERE id=${id} AND user_id=${u.id} AND status='active' RETURNING *`
      if(!rows[0])return c.json({ok:false,error:'Contrato ativo não encontrado.'},404)
      return c.json({ok:true,contract:rows[0]})
    } catch(e) { console.error('cargo_contract_deliver_error',e); return c.json({ok:false,error:'Erro ao concluir contrato.'},500) }
  })
}
