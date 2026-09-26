import { neon } from '@neondatabase/serverless'
import { hashSessionToken, getCookie } from './sharedAuth'

const UUID_RE=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const MAX_COST=100000

async function currentUser(c:any){
  if(!c.env.DATABASE_URL)return null
  const bearer=c.req.header('Authorization')?.replace(/^Bearer\s+/i,'').trim()
  const token=bearer||getCookie(c.req.raw,'truckhub_session')
  if(!token)return null
  const sql=neon(c.env.DATABASE_URL)
  const rows=await sql`SELECT u.id,u.name,u.email FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${await hashSessionToken(token)} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND u.status='active' LIMIT 1`
  return rows[0]??null
}
const unauthorized=(c:any)=>c.json({ok:false,error:'Sessão inválida ou expirada.'},401)
const num=(v:any,d=0)=>{const n=Number(v);return Number.isFinite(n)?n:d}
const clean=(v:any,max=255)=>String(v??'').trim().slice(0,max)
const clampWear=(v:any)=>Math.min(1,Math.max(0,num(v,0)))

export function registerMaintenanceRoutes(app:any){
  app.get('/me/maintenance',async(c:any)=>{
    const user=await currentUser(c);if(!user)return unauthorized(c)
    try{
      const truckId=clean(c.req.query('truckId'),64)
      const sql=neon(c.env.DATABASE_URL)
      const truck=truckId&&UUID_RE.test(truckId)
        ? await sql`SELECT id,truck_name,brand,model,license_plate,current_odometer_km,current_fuel_l,wear_pct,operational_state,last_telemetry_at,last_maintenance_at,last_maintenance_odometer_km FROM trucks WHERE id=${truckId} AND user_id=${user.id} LIMIT 1`
        : await sql`SELECT t.id,t.truck_name,t.brand,t.model,t.license_plate,t.current_odometer_km,t.current_fuel_l,t.wear_pct,t.operational_state,t.last_telemetry_at,t.last_maintenance_at,t.last_maintenance_odometer_km FROM trucks t JOIN garage_assignments g ON g.truck_id=t.id WHERE g.user_id=${user.id} AND g.active=TRUE ORDER BY g.created_at DESC LIMIT 1`
      const records=truck[0]?await sql`SELECT id,truck_id,service_type,component,description,cost_brl,odometer_km,wear_engine,wear_transmission,wear_cabin,wear_chassis,wear_wheels,created_at FROM truck_maintenance_records WHERE user_id=${user.id} AND truck_id=${truck[0].id} ORDER BY created_at DESC LIMIT 60`:await sql`SELECT id,truck_id,service_type,component,description,cost_brl,odometer_km,wear_engine,wear_transmission,wear_cabin,wear_chassis,wear_wheels,created_at FROM truck_maintenance_records WHERE user_id=${user.id} ORDER BY created_at DESC LIMIT 60`
      const totals=truck[0]?await sql`SELECT COUNT(*)::int services,COALESCE(SUM(cost_brl),0) cost_brl,MAX(created_at) last_service_at FROM truck_maintenance_records WHERE user_id=${user.id} AND truck_id=${truck[0].id}`:await sql`SELECT COUNT(*)::int services,COALESCE(SUM(cost_brl),0) cost_brl,MAX(created_at) last_service_at FROM truck_maintenance_records WHERE user_id=${user.id}`
      return c.json({ok:true,truck:truck[0]??null,summary:totals[0]??{services:0,cost_brl:0,last_service_at:null},records},{headers:{'Cache-Control':'no-store'}})
    }catch(error){console.error('maintenance_load_error',error);return c.json({ok:false,error:'Erro ao carregar a manutenção.'},500)}
  })

  app.post('/me/maintenance',async(c:any)=>{
    const user=await currentUser(c);if(!user)return unauthorized(c)
    try{
      const body=await c.req.json().catch(()=>null) as any
      let truckId=clean(body?.truckId,64)
      const licensePlate=clean(body?.licensePlate,40)
      const sql=neon(c.env.DATABASE_URL)
      if(!UUID_RE.test(truckId)){
        // O desktop pode registrar a manutenção offline sem conhecer o UUID da
        // garagem. Resolva apenas dentro da frota do próprio usuário e somente
        // quando a placa identificar exatamente um caminhão.
        if(!licensePlate)return c.json({ok:false,error:'Caminhão inválido.'},400)
        const matches=await sql`SELECT id FROM trucks WHERE user_id=${user.id} AND REGEXP_REPLACE(UPPER(COALESCE(license_plate,'')),'[^A-Z0-9]','','g')=REGEXP_REPLACE(UPPER(${licensePlate}),'[^A-Z0-9]','','g') LIMIT 2`
        if(matches.length!==1)return c.json({ok:false,error:'Não foi possível identificar unicamente o caminhão da manutenção.'},409)
        truckId=String(matches[0].id)
      }
      const serviceType=clean(body?.serviceType,60)||'Manutenção'
      const component=clean(body?.component,60)||'Geral'
      const description=clean(body?.description,255)||null
      const cost=Math.max(0,Math.min(MAX_COST,num(body?.costBrl,0)))
      const odometer=Math.max(0,num(body?.odometerKm,0))
      const sourceKey=clean(body?.sourceKey,180)||null
      const truck=await sql`SELECT id FROM trucks WHERE id=${truckId} AND user_id=${user.id} LIMIT 1`
      if(!truck[0])return c.json({ok:false,error:'Caminhão não pertence a este motorista.'},404)
      if(sourceKey){
        const existing=await sql`SELECT id FROM truck_maintenance_records WHERE user_id=${user.id} AND source_key=${sourceKey} LIMIT 1`
        if(existing[0])return c.json({ok:true,duplicate:true,id:existing[0].id})
      }
      // Uma manutenção com custo é uma única operação de negócio: registro técnico
      // + despesa precisam confirmar juntos. A função SQL usa source_key como chave
      // idempotente e impede estados parciais em retries/offline.
      const applied=await sql`SELECT * FROM apply_maintenance_service(
        ${user.id}::uuid,${truckId}::uuid,${sourceKey},${serviceType},${component},
        ${description},${cost},${odometer},${clampWear(body?.wearEngine)},
        ${clampWear(body?.wearTransmission)},${clampWear(body?.wearCabin)},
        ${clampWear(body?.wearChassis)},${clampWear(body?.wearWheels)}
      )`
      const result=applied[0]
      if(!result)return c.json({ok:false,error:'Falha ao confirmar a manutenção.'},500)
      return c.json({ok:true,duplicate:!!result.duplicate,id:result.record_id},{status:result.duplicate?200:201})
    }catch(error){console.error('maintenance_create_error',error);return c.json({ok:false,error:'Erro ao registrar manutenção.'},500)}
  })
}
