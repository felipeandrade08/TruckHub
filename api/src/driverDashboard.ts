import { neon } from '@neondatabase/serverless'

interface Env { DATABASE_URL?: string }
const SESSION_COOKIE='truckhub_session'
const UUID_RE=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const enc=new TextEncoder()
async function hash(value:string){const d=await crypto.subtle.digest('SHA-256',enc.encode(value));let b='';for(const x of new Uint8Array(d))b+=String.fromCharCode(x);return btoa(b)}
function cookie(req:Request){const raw=req.headers.get('Cookie')??'';for(const p of raw.split(';')){const [k,...v]=p.trim().split('=');if(k===SESSION_COOKIE){try{return decodeURIComponent(v.join('='))}catch{return v.join('=')}}}return null}
async function user(c:any){if(!c.env.DATABASE_URL)return null;const token=cookie(c.req.raw)||((c.req.header('Authorization')??'').replace(/^Bearer\s+/i,'').trim()||null);if(!token)return null;const sql=neon(c.env.DATABASE_URL);const rows=await sql`SELECT u.id,u.name,u.email FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${await hash(token)} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND s.session_type IN ('web','desktop') AND u.status='active' LIMIT 1`;return rows[0]??null}
function err(message:string,status:number){return new Response(JSON.stringify({ok:false,error:message}),{status,headers:{'content-type':'application/json; charset=UTF-8','cache-control':'no-store'}})}
function text(v:any,max:number){const s=String(v??'').trim();return s?s.slice(0,max):null}

export function registerDriverDashboardRoutes(app:any){
  app.get('/me/trucks',async c=>{const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const sql=neon(c.env.DATABASE_URL!);const rows=await sql`SELECT id,truck_name,brand,model,license_plate,created_at,updated_at FROM trucks WHERE user_id=${u.id} ORDER BY created_at ASC`;return c.json({ok:true,trucks:rows},{headers:{'Cache-Control':'no-store'}})})
  app.post('/me/trucks',async c=>{const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const d=await c.req.json().catch(()=>null) as any;if(!d)return err('JSON inválido.',400);const brand=text(d.brand,80),model=text(d.model,120),truckName=text(d.truckName,120),plate=text(d.licensePlate,32);if(!brand||!model)return err('Marca e modelo são obrigatórios.',400);const sql=neon(c.env.DATABASE_URL!);const rows=await sql`INSERT INTO trucks(user_id,truck_name,brand,model,license_plate) VALUES(${u.id},${truckName},${brand},${model},${plate}) RETURNING id,truck_name,brand,model,license_plate,created_at,updated_at`;return c.json({ok:true,truck:rows[0]},201)})
  app.delete('/me/trucks/:id',async c=>{const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const id=c.req.param('id');if(!UUID_RE.test(id))return err('Caminhão não encontrado.',404);const sql=neon(c.env.DATABASE_URL!);const rows=await sql`DELETE FROM trucks WHERE id=${id} AND user_id=${u.id} RETURNING id`;if(!rows[0])return err('Caminhão não encontrado.',404);return c.json({ok:true})})
  app.get('/me/financial-summary',async c=>{
    const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const sql=neon(c.env.DATABASE_URL!);
    const totals=await sql`SELECT COALESCE(SUM(s.driver_gross),0)::numeric revenue,COALESCE(SUM(s.driver_expenses),0)::numeric expenses,
      COALESCE(SUM(s.loan_payment),0)::numeric loan_payments,COALESCE(SUM(s.driver_net),0)::numeric result,
      COALESCE(SUM(t.distance_km),0)::numeric distance,COALESCE(SUM(t.fuel_used_l),0)::numeric fuel,COUNT(*)::int trips
      FROM company_trip_settlements s JOIN trips t ON t.id=s.trip_id WHERE s.user_id=${u.id}`;
    const monthly=await sql`SELECT TO_CHAR(date_trunc('month',s.settled_at),'YYYY-MM') month,COALESCE(SUM(s.driver_gross),0)::numeric revenue,
      COALESCE(SUM(s.driver_expenses),0)::numeric expenses,COALESCE(SUM(s.loan_payment),0)::numeric loan_payments,
      COALESCE(SUM(s.driver_net),0)::numeric result,COUNT(*)::int trips FROM company_trip_settlements s
      WHERE s.user_id=${u.id} AND s.settled_at>=date_trunc('month',NOW())-INTERVAL '5 months' GROUP BY 1 ORDER BY 1`;
    const topTrips=await sql`SELECT t.id,t.origin,t.destination,t.cargo,t.started_at,t.distance_km,s.driver_gross revenue,s.driver_expenses expenses,
      s.loan_payment,s.driver_net result FROM company_trip_settlements s JOIN trips t ON t.id=s.trip_id
      WHERE s.user_id=${u.id} ORDER BY s.driver_net DESC LIMIT 5`;
    const r=totals[0]||{},revenue=Number(r.revenue||0),expenses=Number(r.expenses||0),result=Number(r.result||0),distance=Number(r.distance||0),fuel=Number(r.fuel||0),tripCount=Number(r.trips||0);
    return c.json({ok:true,summary:{revenue:Number(revenue.toFixed(2)),expenses:Number(expenses.toFixed(2)),loanPayments:Number(r.loan_payments||0),result:Number(result.toFixed(2)),distance:Number(distance.toFixed(1)),fuel:Number(fuel.toFixed(1)),trips:tripCount,averageResultPerTrip:tripCount?Number((result/tripCount).toFixed(2)):0,averageKmPerL:fuel>0?Number((distance/fuel).toFixed(2)):null,monthly:monthly.map(x=>({...x,revenue:Number(x.revenue||0),expenses:Number(x.expenses||0),loanPayments:Number(x.loan_payments||0),result:Number(x.result||0),trips:Number(x.trips||0)})),topTrips:topTrips.map(x=>({...x,revenue:Number(x.revenue||0),expenses:Number(x.expenses||0),loanPayment:Number(x.loan_payment||0),result:Number(x.result||0)})),source:'company_trip_settlements'}},{headers:{'Cache-Control':'no-store'}})
  })
  app.get('/me/dashboard-advanced',async c=>{
    const u=await user(c);if(!u)return err('Sessão inválida ou expirada.',401);const raw=String(c.req.query('period')??'30'),days=raw==='all'?null:Math.min(365,Math.max(1,parseInt(raw,10)||30));const sql=neon(c.env.DATABASE_URL!),since=days===null?null:new Date(Date.now()-days*86400000);
    const rows=since===null?await sql`SELECT t.id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,t.distance_km,t.fuel_used_l,s.driver_gross,s.driver_expenses,s.loan_payment,s.driver_net FROM company_trip_settlements s JOIN trips t ON t.id=s.trip_id WHERE s.user_id=${u.id} ORDER BY s.settled_at DESC LIMIT 500`:await sql`SELECT t.id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,t.distance_km,t.fuel_used_l,s.driver_gross,s.driver_expenses,s.loan_payment,s.driver_net FROM company_trip_settlements s JOIN trips t ON t.id=s.trip_id WHERE s.user_id=${u.id} AND s.settled_at>=${since.toISOString()} ORDER BY s.settled_at DESC LIMIT 500`;
    const trips=rows.map(x=>({...x,revenue_brl:Number(x.driver_gross||0),expenses_brl:Number(x.driver_expenses||0),loan_payment_brl:Number(x.loan_payment||0),result_brl:Number(x.driver_net||0)}));const km=trips.reduce((s,x)=>s+Number(x.distance_km||0),0),fuel=trips.reduce((s,x)=>s+Number(x.fuel_used_l||0),0),revenue=trips.reduce((s,x)=>s+x.revenue_brl,0),spent=trips.reduce((s,x)=>s+x.expenses_brl,0),loanPayments=trips.reduce((s,x)=>s+x.loan_payment_brl,0),result=trips.reduce((s,x)=>s+x.result_brl,0);
    return c.json({ok:true,driver:{id:u.id,name:u.name,email:u.email},periodDays:days,trips,kpis:{distance:Number(km.toFixed(1)),fuel:Number(fuel.toFixed(1)),trips:trips.length,revenue:Number(revenue.toFixed(2)),expenses:Number(spent.toFixed(2)),loanPayments:Number(loanPayments.toFixed(2)),result:Number(result.toFixed(2)),averageKmPerL:fuel>0?Number((km/fuel).toFixed(2)):null,averageResultPerTrip:trips.length?Number((result/trips.length).toFixed(2)):0},financialSource:'company_trip_settlements',updatedAt:new Date().toISOString()},{headers:{'Cache-Control':'no-store'}})
  })

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
      let rows:any[]=[];
      if(companyId){
        if(since===null){
          rows=await sql`WITH members AS (SELECT u.id,u.name,u.email FROM company_members cm JOIN users u ON u.id=cm.user_id WHERE cm.company_id=${companyId} AND cm.role='driver' AND cm.status='active' AND u.status='active'), incomes AS (SELECT l.trip_id,MAX(l.amount_brl)::numeric AS gross_brl FROM economy_ledger l WHERE l.entry_type='trip_income' AND l.amount_brl>0 GROUP BY l.trip_id) SELECT m.id,m.name,m.email,COUNT(t.id)::int AS trips,COALESCE(SUM(t.distance_km),0)::numeric AS km,COALESCE(SUM(COALESCE(i.gross_brl,t.cargo_value_brl,0)),0)::numeric AS revenue_brl FROM members m LEFT JOIN trips t ON t.user_id=m.id AND t.status='finished' LEFT JOIN incomes i ON i.trip_id=t.id GROUP BY m.id,m.name,m.email`;
        }else{
          rows=await sql`WITH members AS (SELECT u.id,u.name,u.email FROM company_members cm JOIN users u ON u.id=cm.user_id WHERE cm.company_id=${companyId} AND cm.role='driver' AND cm.status='active' AND u.status='active'), incomes AS (SELECT l.trip_id,MAX(l.amount_brl)::numeric AS gross_brl FROM economy_ledger l WHERE l.entry_type='trip_income' AND l.amount_brl>0 GROUP BY l.trip_id) SELECT m.id,m.name,m.email,COUNT(t.id)::int AS trips,COALESCE(SUM(t.distance_km),0)::numeric AS km,COALESCE(SUM(COALESCE(i.gross_brl,t.cargo_value_brl,0)),0)::numeric AS revenue_brl FROM members m LEFT JOIN trips t ON t.user_id=m.id AND t.status='finished' AND COALESCE(t.finished_at,t.started_at)>=${since.toISOString()} LEFT JOIN incomes i ON i.trip_id=t.id GROUP BY m.id,m.name,m.email`;
        }
      }else{
        if(since===null){
          rows=await sql`WITH incomes AS (SELECT l.trip_id,MAX(l.amount_brl)::numeric AS gross_brl FROM economy_ledger l WHERE l.entry_type='trip_income' AND l.amount_brl>0 GROUP BY l.trip_id) SELECT u.id,u.name,u.email,COUNT(t.id)::int AS trips,COALESCE(SUM(t.distance_km),0)::numeric AS km,COALESCE(SUM(COALESCE(i.gross_brl,t.cargo_value_brl,0)),0)::numeric AS revenue_brl FROM users u LEFT JOIN trips t ON t.user_id=u.id AND t.status='finished' LEFT JOIN incomes i ON i.trip_id=t.id WHERE u.id=${u.id} GROUP BY u.id,u.name,u.email`;
        }else{
          rows=await sql`WITH incomes AS (SELECT l.trip_id,MAX(l.amount_brl)::numeric AS gross_brl FROM economy_ledger l WHERE l.entry_type='trip_income' AND l.amount_brl>0 GROUP BY l.trip_id) SELECT u.id,u.name,u.email,COUNT(t.id)::int AS trips,COALESCE(SUM(t.distance_km),0)::numeric AS km,COALESCE(SUM(COALESCE(i.gross_brl,t.cargo_value_brl,0)),0)::numeric AS revenue_brl FROM users u LEFT JOIN trips t ON t.user_id=u.id AND t.status='finished' AND COALESCE(t.finished_at,t.started_at)>=${since.toISOString()} LEFT JOIN incomes i ON i.trip_id=t.id WHERE u.id=${u.id} GROUP BY u.id,u.name,u.email`;
        }
      }
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
