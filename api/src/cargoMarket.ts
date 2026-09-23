import { neon } from '@neondatabase/serverless'
import { hashSessionToken, getCookie } from './sharedAuth'

const RATE_MIN = 5
const RATE_MAX = 12
const MARKET_CYCLE_MINUTES = 59

function normalize(value: any) { return String(value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim() }
function slug(value: string) { return normalize(value).replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 70) || 'carga_geral' }
function hash(key: string) { let h = 0; for (const ch of key) h = (h * 31 + ch.charCodeAt(0)) % 100000; return h }
function statusFor(rate: number) { if (rate >= 9) return 'high'; if (rate <= 6.5) return 'low'; return 'normal' }
function marketCycle(now = Date.now()) {
  const cycleMs = MARKET_CYCLE_MINUTES * 60 * 1000
  const index = Math.floor(now / cycleMs)
  const nextAt = new Date((index + 1) * cycleMs)
  return { index, nextAt }
}
function dynamicRate(key: string, baseRate: number, cycleIndex: number) {
  const seed = hash(`${key}|${cycleIndex}`)
  const offsetSteps = (seed % 9) - 4
  const raw = baseRate + offsetSteps * 0.5
  return Number(Math.min(RATE_MAX, Math.max(RATE_MIN, raw)).toFixed(2))
}
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
      const cycle=marketCycle()
      const offers=rows.map((row:any)=>{
        const baseRate=Number(row.rate_brl_km)||RATE_MIN
        const rate=dynamicRate(String(row.cargo_key),baseRate,cycle.index)
        const previous=dynamicRate(String(row.cargo_key),baseRate,cycle.index-1)
        return {...row,base_rate_brl_km:baseRate,rate_brl_km:rate,previous_rate_brl_km:previous,market_status:statusFor(rate),trend:rate>previous?'up':rate<previous?'down':'stable'}
      }).sort((a:any,b:any)=>Number(b.rate_brl_km)-Number(a.rate_brl_km)||String(a.display_name).localeCompare(String(b.display_name)))
      return c.json({ok:true,policy:{minimumBrlKm:RATE_MIN,maximumBrlKm:RATE_MAX,pricing:'dynamic_59m',cycleMinutes:MARKET_CYCLE_MINUTES,nextRefreshAt:cycle.nextAt.toISOString()},offers,dashboard:{popularCargo:popularCargo[0]??null,activeDriver:activeDriver[0]??null,trailerUsage:trailerUsage[0]??null}},{headers:{'Cache-Control':'no-store'}})
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

  // Contratos são o vínculo econômico da carga detectada com a viagem.
  // A tarifa fica congelada no contrato e não muda se o catálogo for alterado depois.
  app.get('/me/cargo-market/contracts', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const sql=neon(c.env.DATABASE_URL)
      const rows=await sql`
        SELECT
          c.id,c.offer_id,c.cargo_key,c.cargo,c.rate_brl_km,c.status,
          c.origin,c.destination,c.distance_km,c.cargo_mass_kg,c.bonus_brl,
          c.damage_penalty_pct,c.clean_delivery_bonus_pct,
          c.accepted_at,c.started_at,c.delivered_at,c.trip_id,
          o.market_status,o.discovered_count
        FROM cargo_contracts c
        LEFT JOIN cargo_market_offers o ON o.id=c.offer_id
        WHERE c.user_id=${u.id}
        ORDER BY
          CASE c.status WHEN 'active' THEN 0 WHEN 'accepted' THEN 1 WHEN 'delivered' THEN 2 ELSE 3 END,
          c.accepted_at DESC
        LIMIT 100
      `
      return c.json({ok:true,contracts:rows},{headers:{'Cache-Control':'no-store'}})
    } catch(e) {
      console.error('cargo_contracts_load_error',e)
      return c.json({ok:false,error:'Erro ao carregar os contratos de carga.'},500)
    }
  })

  app.post('/me/cargo-market/contracts', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const body=await c.req.json().catch(()=>null) as any
      const cargoName=text(body?.cargo,180)
      const origin=text(body?.origin,180)
      const destination=text(body?.destination,180)
      const distance=Number(body?.distance_km)
      const mass=Number(body?.cargo_mass_kg)
      if(!cargoName)return c.json({ok:false,error:'Informe a carga do contrato.'},400)

      const sql=neon(c.env.DATABASE_URL)
      const offer=await ensureCargo(sql,cargoName)
      const baseRate=Number(offer?.rate_brl_km)||RATE_MIN
      const rate=dynamicRate(String(offer?.cargo_key ?? slug(cargoName)),baseRate,marketCycle().index)
      const distanceValue=Number.isFinite(distance)&&distance>=0?distance:null
      const massValue=Number.isFinite(mass)&&mass>=0?mass:null

      const rows=await sql`
        INSERT INTO cargo_contracts(
          user_id,offer_id,cargo_key,cargo,rate_brl_km,status,
          origin,destination,distance_km,cargo_mass_kg,accepted_at
        )
        VALUES(
          ${u.id},${offer.id},${offer.cargo_key},${offer.display_name},${rate},'accepted',
          ${origin},${destination},${distanceValue},${massValue},NOW()
        )
        RETURNING id,offer_id,cargo_key,cargo,rate_brl_km,status,origin,destination,distance_km,cargo_mass_kg,bonus_brl,damage_penalty_pct,clean_delivery_bonus_pct,accepted_at,started_at,delivered_at,trip_id
      `
      return c.json({ok:true,contract:rows[0]},{headers:{'Cache-Control':'no-store'}})
    } catch(e) {
      console.error('cargo_contract_create_error',e)
      return c.json({ok:false,error:'Não foi possível criar o contrato de carga.'},500)
    }
  })

  app.post('/me/cargo-market/contracts/:id/link-trip', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const id=c.req.param('id')
      const body=await c.req.json().catch(()=>null) as any
      const tripId=text(body?.trip_id,80)
      if(!tripId)return c.json({ok:false,error:'Informe a viagem para vincular ao contrato.'},400)

      const sql=neon(c.env.DATABASE_URL)
      const trips=await sql`SELECT id,cargo,origin,destination,distance_km,cargo_mass_kg,started_at,status FROM trips WHERE id=${tripId} AND user_id=${u.id} LIMIT 1`
      if(!trips[0])return c.json({ok:false,error:'Viagem não encontrada.'},404)

      const contract=await sql`SELECT id,status,trip_id FROM cargo_contracts WHERE id=${id} AND user_id=${u.id} LIMIT 1`
      if(!contract[0])return c.json({ok:false,error:'Contrato não encontrado.'},404)
      if(contract[0].trip_id && contract[0].trip_id!==tripId)return c.json({ok:false,error:'Este contrato já está vinculado a outra viagem.'},409)
      if(contract[0].status==='delivered' || contract[0].status==='cancelled')return c.json({ok:false,error:'Este contrato não pode mais ser vinculado.'},409)

      const trip=trips[0]
      const rows=await sql`
        UPDATE cargo_contracts
        SET
          status=CASE WHEN ${trip.status}='finished' THEN 'delivered' ELSE 'active' END,
          trip_id=${trip.id},
          origin=COALESCE(${trip.origin},origin),
          destination=COALESCE(${trip.destination},destination),
          distance_km=COALESCE(${trip.distance_km},distance_km),
          cargo_mass_kg=COALESCE(${trip.cargo_mass_kg},cargo_mass_kg),
          started_at=COALESCE(started_at,${trip.started_at})
        WHERE id=${id} AND user_id=${u.id}
        RETURNING id,offer_id,cargo_key,cargo,rate_brl_km,status,origin,destination,distance_km,cargo_mass_kg,bonus_brl,damage_penalty_pct,clean_delivery_bonus_pct,accepted_at,started_at,delivered_at,trip_id
      `
      await sql`UPDATE trips SET cargo_contract_id=${id} WHERE id=${trip.id} AND user_id=${u.id}`
      return c.json({ok:true,contract:rows[0]},{headers:{'Cache-Control':'no-store'}})
    } catch(e) {
      console.error('cargo_contract_link_error',e)
      return c.json({ok:false,error:'Não foi possível vincular o contrato à viagem.'},500)
    }
  })

  app.post('/me/cargo-market/contracts/:id/deliver', async c => {
    const u=await user(c); if(!u)return unauthorized(c)
    try {
      const id=c.req.param('id')
      const sql=neon(c.env.DATABASE_URL)
      const rows=await sql`
        UPDATE cargo_contracts
        SET status='delivered',delivered_at=COALESCE(delivered_at,NOW())
        WHERE id=${id} AND user_id=${u.id} AND status IN ('accepted','active')
        RETURNING id,offer_id,cargo_key,cargo,rate_brl_km,status,origin,destination,distance_km,cargo_mass_kg,bonus_brl,damage_penalty_pct,clean_delivery_bonus_pct,accepted_at,started_at,delivered_at,trip_id
      `
      if(!rows[0])return c.json({ok:false,error:'Contrato não encontrado ou já finalizado.'},404)
      return c.json({ok:true,contract:rows[0]},{headers:{'Cache-Control':'no-store'}})
    } catch(e) {
      console.error('cargo_contract_deliver_error',e)
      return c.json({ok:false,error:'Não foi possível concluir o contrato.'},500)
    }
  })
}
