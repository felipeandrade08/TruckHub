import { neon } from '@neondatabase/serverless'

const SESSION_COOKIE='truckhub_session'
async function hash(value:string){const d=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(value));let b='';for(const x of new Uint8Array(d))b+=String.fromCharCode(x);return btoa(b)}
function cookie(req:Request){const raw=req.headers.get('Cookie')??'';for(const p of raw.split(';')){const [k,...v]=p.trim().split('=');if(k===SESSION_COOKIE){try{return decodeURIComponent(v.join('='))}catch{return v.join('=')}}}return null}
async function currentUser(c:any){if(!c.env.DATABASE_URL)return null;const token=cookie(c.req.raw);if(!token)return null;const sql=neon(c.env.DATABASE_URL);const rows=await sql`SELECT u.id,u.name,u.email FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${await hash(token)} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND s.session_type='web' AND u.status='active' LIMIT 1`;return rows[0]??null}
function err(message:string,status:number){return new Response(JSON.stringify({ok:false,error:message}),{status,headers:{'content-type':'application/json; charset=UTF-8','cache-control':'no-store'}})}
export function registerStatisticsRoutes(app:any){
  app.get('/me/statistics',async c=>{try{const u=await currentUser(c);if(!u)return err('Sessão inválida ou expirada.',401);const sql=neon(c.env.DATABASE_URL!);
    const trips=await sql`SELECT COUNT(*)::int AS trips,COALESCE(SUM(distance_km),0) AS distance_km,COALESCE(SUM(cargo_mass_kg),0) AS tons_kg,COALESCE(SUM(fuel_used_l),0) AS fuel_l,COALESCE(SUM(cargo_value_brl),0) AS revenue_brl,COALESCE(SUM(CASE WHEN cargo_damage=0 THEN 1 ELSE 0 END),0)::int AS clean_deliveries,COALESCE(SUM(CASE WHEN cargo_damage>0 THEN 1 ELSE 0 END),0)::int AS damaged_deliveries FROM trips WHERE user_id=${u.id} AND status='finished'`;
    const expenses=await sql`SELECT COALESCE(SUM(amount),0) AS total_brl FROM expenses WHERE user_id=${u.id}`;
    const maintenance=await sql`SELECT COALESCE(SUM(amount),0) AS total_brl FROM expenses WHERE user_id=${u.id} AND LOWER(COALESCE(type,'')) IN ('maintenance','manutencao','manutenção')`;
    const fuelExpenses=await sql`SELECT COALESCE(SUM(amount),0) AS total_brl FROM expenses WHERE user_id=${u.id} AND LOWER(COALESCE(type,'')) IN ('fuel','combustivel','combustível')`;
    const row=trips[0]??{};const distance=Number(row.distance_km||0),fuel=Number(row.fuel_l||0),revenue=Number(row.revenue_brl||0),expenseTotal=Number(expenses[0]?.total_brl||0);
    const payload={trips:Number(row.trips||0),distanceKm:Number(distance.toFixed(1)),cargoKg:Number(Number(row.tons_kg||0).toFixed(1)),fuelLiters:Number(fuel.toFixed(1)),averageKmPerLiter:fuel>0?Number((distance/fuel).toFixed(2)):null,averageFuelL100:distance>0?Number((fuel/distance*100).toFixed(2)):null,revenueBrl:Number(revenue.toFixed(2)),expensesBrl:Number(expenseTotal.toFixed(2)),profitBrl:Number((revenue-expenseTotal).toFixed(2)),fuelExpensesBrl:Number(Number(fuelExpenses[0]?.total_brl||0).toFixed(2)),maintenanceBrl:Number(Number(maintenance[0]?.total_brl||0).toFixed(2)),cleanDeliveries:Number(row.clean_deliveries||0),damagedDeliveries:Number(row.damaged_deliveries||0)};
    return c.json({ok:true,statistics:payload},{headers:{'Cache-Control':'no-store'}})
  }catch(e){console.error('statistics_error',e);return err('Erro ao carregar estatísticas.',500)}})
}
