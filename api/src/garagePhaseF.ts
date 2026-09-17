import { neon } from '@neondatabase/serverless'
import { hashSessionToken, getCookie } from './sharedAuth'

async function currentUser(c:any){
  if(!c.env.DATABASE_URL)return null
  const bearer=c.req.header('Authorization')?.replace(/^Bearer\s+/i,'').trim()
  const token=bearer||getCookie(c.req.raw,'truckhub_session')
  if(!token)return null
  const sql=neon(c.env.DATABASE_URL)
  const rows=await sql`SELECT u.id,u.name,u.email FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${await hashSessionToken(token)} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND u.status='active' LIMIT 1`
  return rows[0]??null
}
function unauthorized(c:any){return c.json({ok:false,error:'Sessão inválida ou expirada.'},401)}

export function registerGaragePhaseFRoutes(app:any){
  app.get('/me/garage/fleet',async(c:any)=>{
    const user=await currentUser(c);if(!user)return unauthorized(c)
    try{
      const sql=neon(c.env.DATABASE_URL)
      const rows=await sql`SELECT g.id AS assignment_id,g.exclusive,g.active,g.label,g.skin_code,g.truck_key,g.last_seen_at,t.id AS truck_id,t.truck_name,t.brand,t.model,t.license_plate,t.current_odometer_km,t.current_fuel_l,t.wear_pct,t.operational_state,t.last_telemetry_at,COALESCE(stats.trips_completed,0)::int AS trips_completed,COALESCE(stats.distance_km,0)::numeric AS distance_used_km,stats.last_used_at FROM garage_assignments g JOIN trucks t ON t.id=g.truck_id LEFT JOIN LATERAL (SELECT COUNT(*) FILTER(WHERE h.finished_at IS NOT NULL) AS trips_completed,COALESCE(SUM(h.distance_km),0) AS distance_km,MAX(h.finished_at) AS last_used_at FROM garage_usage_history h WHERE h.truck_id=t.id AND h.user_id=${user.id}) stats ON TRUE WHERE g.user_id=${user.id} AND g.active=TRUE ORDER BY g.created_at DESC`
      return c.json({ok:true,owner:{id:user.id,name:user.name,email:user.email},trucks:rows},{headers:{'Cache-Control':'no-store'}})
    }catch(error){console.error('garage_fleet_error',error);return c.json({ok:false,error:'Erro ao carregar o estado da garagem.'},500)}
  })

  app.get('/me/garage/:truckId/history',async(c:any)=>{
    const user=await currentUser(c);if(!user)return unauthorized(c)
    const truckId=c.req.param('truckId')
    try{
      const sql=neon(c.env.DATABASE_URL)
      const rows=await sql`SELECT h.id,h.trip_id,h.started_at,h.finished_at,h.start_odometer_km,h.end_odometer_km,h.distance_km,h.start_fuel_l,h.end_fuel_l,h.cargo,h.origin,h.destination,t.brand,t.model,t.license_plate FROM garage_usage_history h JOIN trucks t ON t.id=h.truck_id WHERE h.user_id=${user.id} AND h.truck_id=${truckId} ORDER BY h.started_at DESC LIMIT 100`
      return c.json({ok:true,history:rows},{headers:{'Cache-Control':'no-store'}})
    }catch(error){console.error('garage_history_error',error);return c.json({ok:false,error:'Erro ao carregar o histórico do caminhão.'},500)}
  })

  app.get('/me/garage/usage-summary',async(c:any)=>{
    const user=await currentUser(c);if(!user)return unauthorized(c)
    try{
      const sql=neon(c.env.DATABASE_URL)
      const totals=await sql`SELECT COUNT(*)::int AS trips,COALESCE(SUM(distance_km),0)::numeric AS distance_km,COALESCE(SUM(GREATEST(0,COALESCE(start_fuel_l,0)-COALESCE(end_fuel_l,0))),0)::numeric AS fuel_used_l FROM garage_usage_history WHERE user_id=${user.id}`
      const byTruck=await sql`SELECT t.id AS truck_id,t.brand,t.model,t.license_plate,COUNT(h.id)::int AS trips,COALESCE(SUM(h.distance_km),0)::numeric AS distance_km,MAX(h.finished_at) AS last_used_at FROM garage_usage_history h JOIN trucks t ON t.id=h.truck_id WHERE h.user_id=${user.id} GROUP BY t.id,t.brand,t.model,t.license_plate ORDER BY distance_km DESC`
      return c.json({ok:true,summary:totals[0]??{trips:0,distance_km:0,fuel_used_l:0},byTruck},{headers:{'Cache-Control':'no-store'}})
    }catch(error){console.error('garage_usage_summary_error',error);return c.json({ok:false,error:'Erro ao carregar o uso da garagem.'},500)}
  })
}
