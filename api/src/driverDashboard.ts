import { neon } from '@neondatabase/serverless'

interface Env { DATABASE_URL?: string }
const SESSION_COOKIE='truckhub_session'
const UUID_RE=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const enc=new TextEncoder()
async function hash(value:string){const d=await crypto.subtle.digest('SHA-256',enc.encode(value));let b='';for(const x of new Uint8Array(d))b+=String.fromCharCode(x);return btoa(b)}
function cookie(req:Request){const raw=req.headers.get('Cookie')??'';for(const p of raw.split(';')){const [k,...v]=p.trim().split('=');if(k===SESSION_COOKIE){try{return decodeURIComponent(v.join('='))}catch{return v.join('=')}}}return null}
async function user(c:any){if(!c.env.DATABASE_URL)return null;const token=cookie(c.req.raw)||((c.req.header('Authorization')??'').replace(/^Bearer\\s+/i,'').trim()||null);if(!token)return null;const sql=neon(c.env.DATABASE_URL);const rows=await sql`SELECT u.id,u.name,u.email FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${await hash(token)} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND s.session_type IN ('web','desktop') AND u.status='active' LIMIT 1`;return rows[0]??null}
function err(message:string,status:number){return new Response(JSON.stringify({ok:false,error:message}),{status,headers:{'content-type':'application/json; charset=UTF-8','cache-control':'no-store'}})}
function text(v:any,max:number){const s=String(v??'').trim();return s?s.slice(0,max):null}

export function registerDriverDashboardRoutes(app:any){
  app.get('/me/trucks',async c=>{const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const sql=neon(c.env.DATABASE_URL!);const rows=await sql`SELECT id,truck_name,brand,model,license_plate,created_at,updated_at FROM trucks WHERE user_id=${u.id} ORDER BY created_at ASC`;return c.json({ok:true,trucks:rows},{headers:{'Cache-Control':'no-store'}})})
  app.post('/me/trucks',async c=>{const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const d=await c.req.json().catch(()=>null) as any;if(!d)return err('JSON inválido.',400);const brand=text(d.brand,80),model=text(d.model,120),truckName=text(d.truckName,120),plate=text(d.licensePlate,32);if(!brand||!model)return err('Marca e modelo são obrigatórios.',400);const sql=neon(c.env.DATABASE_URL!);const rows=await sql`INSERT INTO trucks(user_id,truck_name,brand,model,license_plate) VALUES(${u.id},${truckName},${brand},${model},${plate}) RETURNING id,truck_name,brand,model,license_plate,created_at,updated_at`;return c.json({ok:true,truck:rows[0]},201)})
  app.delete('/me/trucks/:id',async c=>{const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const id=c.req.param('id');if(!UUID_RE.test(id))return err('Caminhão não encontrado.',404);const sql=neon(c.env.DATABASE_URL!);const rows=await sql`DELETE FROM trucks WHERE id=${id} AND user_id=${u.id} RETURNING id`;if(!rows[0])return err('Caminhão não encontrado.',404);return c.json({ok:true})})
  app.get('/me/financial-summary',async c=>{const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const sql=neon(c.env.DATABASE_URL!);const totals=await sql`WITH trip_totals AS (SELECT COALESCE(SUM(t.cargo_value_brl) FILTER(WHERE t.status='finished'),0)::numeric AS revenue,COALESCE(SUM(t.distance_km) FILTER(WHERE t.status='finished'),0)::numeric AS distance,COALESCE(SUM(t.fuel_used_l) FILTER(WHERE t.status='finished'),0)::numeric AS fuel,COUNT(*) FILTER(WHERE t.status='finished')::int AS trips FROM trips t WHERE t.user_id=${u.id}),expense_totals AS (SELECT COALESCE(SUM(amount),0)::numeric AS expenses FROM expenses WHERE user_id=${u.id}) SELECT trip_totals.*,expense_totals.expenses FROM trip_totals CROSS JOIN expense_totals`;const byType=await sql`SELECT e.type,COALESCE(SUM(e.amount),0)::numeric AS total,COUNT(*)::int AS count FROM expenses e WHERE e.user_id=${u.id} GROUP BY e.type ORDER BY total DESC`;const monthly=await sql`WITH trip_month AS (SELECT TO_CHAR(date_trunc('month',t.started_at),'YYYY-MM') AS month,COALESCE(SUM(t.cargo_value_brl) FILTER(WHERE t.status='finished'),0)::numeric AS revenue,COUNT(*) FILTER(WHERE t.status='finished')::int AS trips FROM trips t WHERE t.user_id=${u.id} AND t.started_at>=date_trunc('month',NOW())-INTERVAL '5 months' GROUP BY 1),expense_month AS (SELECT TO_CHAR(date_trunc('month',e.created_at),'YYYY-MM') AS month,COALESCE(SUM(e.amount),0)::numeric AS expenses FROM expenses e WHERE e.user_id=${u.id} AND e.created_at>=date_trunc('month',NOW())-INTERVAL '5 months' GROUP BY 1) SELECT COALESCE(t.month,e.month) AS month,COALESCE(t.revenue,0)::numeric AS revenue,COALESCE(e.expenses,0)::numeric AS expenses,COALESCE(t.trips,0)::int AS trips FROM trip_month t FULL OUTER JOIN expense_month e ON e.month=t.month ORDER BY month ASC`;const topTrips=await sql`SELECT t.id,t.origin,t.destination,t.cargo,t.started_at,t.distance_km,t.cargo_value_brl,COALESCE(SUM(e.amount),0)::numeric AS expenses FROM trips t LEFT JOIN expenses e ON e.trip_id=t.id AND e.user_id=t.user_id WHERE t.user_id=${u.id} AND t.status='finished' GROUP BY t.id ORDER BY (COALESCE(t.cargo_value_brl,0)-COALESCE(SUM(e.amount),0)) DESC LIMIT 5`;const r=totals[0]||{};const revenue=Number(r.revenue||0),expenses=Number(r.expenses||0),distance=Number(r.distance||0),fuel=Number(r.fuel||0),tripCount=Number(r.trips||0);return c.json({ok:true,summary:{revenue:Number(revenue.toFixed(2)),expenses:Number(expenses.toFixed(2)),result:Number((revenue-expenses).toFixed(2)),distance:Number(distance.toFixed(1)),fuel:Number(fuel.toFixed(1)),trips:tripCount,averageResultPerTrip:tripCount?Number(((revenue-expenses)/tripCount).toFixed(2)):0,averageKmPerL:fuel>0?Number((distance/fuel).toFixed(2)):null,expenseByType:byType.map(x=>({type:x.type,total:Number(x.total||0),count:Number(x.count||0)})),monthly:monthly.map(x=>({month:x.month,revenue:Number(x.revenue||0),expenses:Number(x.expenses||0),result:Number((Number(x.revenue||0)-Number(x.expenses||0)).toFixed(2)),trips:Number(x.trips||0)})),topTrips:topTrips.map(x=>({id:x.id,origin:x.origin,destination:x.destination,cargo:x.cargo,startedAt:x.started_at,distance:Number(x.distance_km||0),revenue:x.cargo_value_brl==null?null:Number(x.cargo_value_brl),expenses:Number(x.expenses||0),result:x.cargo_value_brl==null?null:Number((Number(x.cargo_value_brl)-Number(x.expenses||0)).toFixed(2))}))}},{headers:{'Cache-Control':'no-store'}})})
  app.get('/me/dashboard-advanced',async c=>{const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const q=c.req.query();const rawPeriod=q.period??'30';const days=rawPeriod==='all'?null:Math.min(365,Math.max(1,Number.parseInt(rawPeriod,10)||30));const sql=neon(c.env.DATABASE_URL!);const since=days===null?null:new Date(Date.now()-days*86400000);const trips=days===null?await sql`SELECT t.id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,t.distance_km,t.fuel_used_l,t.status,t.cargo_value_brl,COALESCE(SUM(e.amount),0)::numeric expenses_brl FROM trips t LEFT JOIN expenses e ON e.trip_id=t.id AND e.user_id=t.user_id WHERE t.user_id=${u.id} AND t.status='finished' GROUP BY t.id ORDER BY t.finished_at DESC LIMIT 500`:await sql`SELECT t.id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,t.distance_km,t.fuel_used_l,t.status,t.cargo_value_brl,COALESCE(SUM(e.amount),0)::numeric expenses_brl FROM trips t LEFT JOIN expenses e ON e.trip_id=t.id AND e.user_id=t.user_id WHERE t.user_id=${u.id} AND t.status='finished' AND COALESCE(t.finished_at,t.started_at)>=${since!.toISOString()} GROUP BY t.id ORDER BY t.finished_at DESC LIMIT 500`;const expenses=days===null?await sql`SELECT id,type,amount,created_at,trip_id FROM expenses WHERE user_id=${u.id} ORDER BY created_at DESC LIMIT 1000`:await sql`SELECT id,type,amount,created_at,trip_id FROM expenses WHERE user_id=${u.id} AND created_at>=${since!.toISOString()} ORDER BY created_at DESC LIMIT 1000`;const km=trips.reduce((s,t)=>s+Number(t.distance_km||0),0),fuel=trips.reduce((s,t)=>s+Number(t.fuel_used_l||0),0),revenue=trips.reduce((s,t)=>s+(t.cargo_value_brl==null?0:Number(t.cargo_value_brl)),0),spent=expenses.reduce((s,e)=>s+Number(e.amount||0),0);const byType:any={};expenses.forEach(e=>{byType[e.type]=(byType[e.type]||0)+Number(e.amount||0)});const routeMap:any={};trips.forEach(t=>{const key=`${t.origin||'Origem'} → ${t.destination||'Destino'}`;if(!routeMap[key])routeMap[key]={count:0,km:0,revenue:0};routeMap[key].count++;routeMap[key].km+=Number(t.distance_km||0);routeMap[key].revenue+=t.cargo_value_brl==null?0:Number(t.cargo_value_brl)});const daily:any={};trips.forEach(t=>{const key=new Date(t.finished_at||t.started_at).toISOString().slice(0,10);daily[key]=(daily[key]||0)+Number(t.distance_km||0)});const hours=trips.reduce((s,t)=>s+(t.started_at&&t.finished_at?Math.max(0,new Date(t.finished_at).getTime()-new Date(t.started_at).getTime())/3600000:0),0);const results=trips.map(t=>({...t,expenses_brl:Number(t.expenses_brl||0),result_brl:t.cargo_value_brl==null?null:Number(t.cargo_value_brl)-Number(t.expenses_brl||0)}));return c.json({ok:true,driver:{id:u.id,name:u.name,email:u.email},periodDays:days,trips:results,expenses:expenses.map(e=>({...e,amount:Number(e.amount||0)})),kpis:{distance:Number(km.toFixed(1)),fuel:Number(fuel.toFixed(1)),trips:trips.length,revenue:Number(revenue.toFixed(2)),expenses:Number(spent.toFixed(2)),result:Number((revenue-spent).toFixed(2)),averageKmPerL:fuel>0?Number((km/fuel).toFixed(2)):null,averageResultPerTrip:trips.length?Number(((revenue-spent)/trips.length).toFixed(2)):0,hours:Number(hours.toFixed(2))},expenseByType:byType,routes:Object.entries(routeMap).map(([route,v]:any)=>({route,...v})).sort((a:any,b:any)=>b.km-a.km).slice(0,6),daily:Object.entries(daily).map(([date,distance]:any)=>({date,distance:Number(distance)})).sort((a:any,b:any)=>a.date.localeCompare(b.date)),updatedAt:new Date().toISOString()},{headers:{'Cache-Control':'no-store'}})})

  app.get('/me/ranking',async c=>{
    const u=await user(c); if(!u)return err('Sessão inválida ou expirada.',401);
    const rawPeriod=String(c.req.query('period')??'30').toLowerCase();
    const metric=['km','revenue','rate','trips'].includes(String(c.req.query('metric')??''))?String(c.req.query('metric')):'km';
    const period=rawPeriod==='all'?'all':rawPeriod==='month'?'month':(['7','30','90'].includes(rawPeriod)?rawPeriod:'30');
    const sql=neon(c.env.DATABASE_URL!);
    try{
      const company=await sql`SELECT cm.company_id FROM company_members cm JOIN companies co ON co.id=cm.company_id WHERE cm.user_id=${u.id} AND cm.status='active' AND co.status='active' LIMIT 1`;
      const companyId=company[0]?.company_id??null;
      const since=period==='all'?null:period==='month'?new Date(new Date().getFullYear(),new Date().getMonth(),1):new Date(Date.now()-Number(period)*86400000);
      const rows=companyId?await sql`
        WITH members AS (
          SELECT u.id,u.name,u.email
          FROM company_members cm JOIN users u ON u.id=cm.user_id
          WHERE cm.company_id=${companyId} AND cm.role='driver' AND cm.status='active' AND u.status='active'
        ), incomes AS (
          SELECT l.trip_id,MAX(l.amount_brl)::numeric AS gross_brl
          FROM economy_ledger l
          WHERE l.entry_type='trip_income' AND l.amount_brl>0
          GROUP BY l.trip_id
        )
        SELECT m.id,m.name,m.email,
          COUNT(t.id)::int AS trips,
          COALESCE(SUM(t.distance_km),0)::numeric AS km,
          COALESCE(SUM(COALESCE(i.gross_brl,t.cargo_value_brl,0)),0)::numeric AS revenue_brl
        FROM members m
        LEFT JOIN trips t ON t.user_id=m.id AND t.status='finished' AND (
          ${since} IS NULL OR COALESCE(t.finished_at,t.started_at)>=${since?.toISOString()}
        )
        LEFT JOIN incomes i ON i.trip_id=t.id
        GROUP BY m.id,m.name,m.email
      `:await sql`
        WITH incomes AS (
          SELECT l.trip_id,MAX(l.amount_brl)::numeric AS gross_brl
          FROM economy_ledger l WHERE l.entry_type='trip_income' AND l.amount_brl>0 GROUP BY l.trip_id
        )
        SELECT u.id,u.name,u.email,COUNT(t.id)::int AS trips,
          COALESCE(SUM(t.distance_km),0)::numeric AS km,
          COALESCE(SUM(COALESCE(i.gross_brl,t.cargo_value_brl,0)),0)::numeric AS revenue_brl
        FROM users u LEFT JOIN trips t ON t.user_id=u.id AND t.status='finished' AND (
          ${since} IS NULL OR COALESCE(t.finished_at,t.started_at)>=${since?.toISOString()}
        ) LEFT JOIN incomes i ON i.trip_id=t.id
        WHERE u.id=${u.id} GROUP BY u.id,u.name,u.email
      `;
      const ranking=rows.map((r:any)=>{const km=Number(r.km||0),revenue=Number(r.revenue_brl||0),trips=Number(r.trips||0);return {id:r.id,name:r.name,email:r.email,trips,km:Number(km.toFixed(1)),revenueBrl:Number(revenue.toFixed(2)),rateBrlKm:km>0?Number((revenue/km).toFixed(2)):0}});
      ranking.sort((a:any,b:any)=>{
        const av=metric==='revenue'?a.revenueBrl:metric==='rate'?a.rateBrlKm:metric==='trips'?a.trips:a.km;
        const bv=metric==='revenue'?b.revenueBrl:metric==='rate'?b.rateBrlKm:metric==='trips'?b.trips:b.km;
        return bv-av||b.km-a.km||b.revenueBrl-a.revenueBrl||a.name.localeCompare(b.name,'pt-BR');
      });
      const ranked=ranking.map((r:any,index:number)=>({...r,position:index+1}));
      const mine=ranked.find((r:any)=>r.id===u.id)??null;
      return c.json({ok:true,period,metric,company:{id:companyId,name:'TransPoli'},updatedAt:new Date().toISOString(),me:mine,drivers:ranked},{headers:{'Cache-Control':'no-store'}});
    }catch(error){console.error('driver_ranking_error',error);return err('Erro ao carregar o ranking dos motoristas.',500)}
  });

  
}
