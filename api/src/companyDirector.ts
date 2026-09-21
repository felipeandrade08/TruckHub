import { neon } from '@neondatabase/serverless'

interface Env { DATABASE_URL?: string }

const USER_COOKIE = 'truckhub_session'
const DIRECTOR_DAYS = 7
const PBKDF2_ITERATIONS = 100000
const enc = new TextEncoder()

function json(c:any, data:any, status=200) {
  return c.json(data, { status, headers: { 'Cache-Control': 'no-store' } })
}
function bad(message:string, status=400) {
  return new Response(JSON.stringify({ ok:false, error:message }), {
    status,
    headers: { 'content-type':'application/json; charset=UTF-8', 'cache-control':'no-store' }
  })
}
function normalizeEmail(v:string){ return v.trim().toLowerCase() }
function b64(bytes:Uint8Array){ let s=''; for(const b of bytes)s+=String.fromCharCode(b); return btoa(s) }
function unb64(v:string){ const s=atob(v); const out=new Uint8Array(s.length); for(let i=0;i<s.length;i++)out[i]=s.charCodeAt(i); return out }
async function hashSecret(secret:string){
  const salt=crypto.getRandomValues(new Uint8Array(16))
  const key=await crypto.subtle.importKey('raw',enc.encode(secret),'PBKDF2',false,['deriveBits'])
  const bits=await crypto.subtle.deriveBits({name:'PBKDF2',salt:salt.buffer as ArrayBuffer,iterations:PBKDF2_ITERATIONS,hash:'SHA-256'},key,256)
  return `pbkdf2-sha256$${PBKDF2_ITERATIONS}$${b64(salt)}$${b64(new Uint8Array(bits))}`
}
async function verifySecret(secret:string,stored:string){
  const p=stored.split('$')
  if(p.length!==4||p[0]!=='pbkdf2-sha256')return false
  const iterations=Number(p[1]); if(iterations!==PBKDF2_ITERATIONS)return false
  let salt:Uint8Array,expected:Uint8Array
  try{salt=unb64(p[2]);expected=unb64(p[3])}catch{return false}
  const key=await crypto.subtle.importKey('raw',enc.encode(secret),'PBKDF2',false,['deriveBits'])
  const bits=new Uint8Array(await crypto.subtle.deriveBits({name:'PBKDF2',salt:salt.buffer as ArrayBuffer,iterations,hash:'SHA-256'},key,expected.length*8))
  if(bits.length!==expected.length)return false
  let diff=0; for(let i=0;i<bits.length;i++)diff|=bits[i]^expected[i]
  return diff===0
}
function randomToken(){return b64(crypto.getRandomValues(new Uint8Array(32)))}
async function sha256(v:string){const d=await crypto.subtle.digest('SHA-256',enc.encode(v));return b64(new Uint8Array(d))}
function readCookie(req:Request,name:string){
  const raw=req.headers.get('Cookie')??''
  for(const part of raw.split(';')){
    const [k,...v]=part.trim().split('=')
    if(k===name){try{return decodeURIComponent(v.join('='))}catch{return v.join('=')}}
  }
  return null
}
function userToken(c:any){
  return c.req.header('Authorization')?.replace(/^Bearer\s+/i,'').trim() || readCookie(c.req.raw,USER_COOKIE)
}
async function currentUser(c:any){
  if(!c.env.DATABASE_URL)return null
  const token=userToken(c); if(!token)return null
  const sql=neon(c.env.DATABASE_URL), h=await sha256(token)
  const rows=await sql`SELECT u.id,u.name,u.email
    FROM sessions s JOIN users u ON u.id=s.user_id
    WHERE s.token_hash=${h} AND s.revoked_at IS NULL AND s.expires_at>NOW()
      AND u.status='active' AND s.session_type IN ('web','desktop') LIMIT 1`
  return rows[0]??null
}
async function director(c:any){
  if(!c.env.DATABASE_URL)return null
  const token=c.req.header('Authorization')?.replace(/^Bearer\s+/i,'').trim()
  if(!token)return null
  const sql=neon(c.env.DATABASE_URL), h=await sha256(token)
  const rows=await sql`SELECT d.id AS director_id,d.company_id,d.user_id,d.email,d.status,
      co.id AS company_name
    FROM company_director_sessions s
    JOIN company_directors d ON d.id=s.director_id
    JOIN companies co ON co.id=d.company_id
    WHERE s.token_hash=${h} AND s.revoked_at IS NULL AND s.expires_at>NOW()
      AND d.status='active' AND co.status='active' LIMIT 1`
  if(!rows[0])return null
  await sql`UPDATE company_director_sessions SET last_seen_at=NOW() WHERE token_hash=${h} AND revoked_at IS NULL`
  return rows[0]
}

export function registerCompanyDirectorRoutes(app:any){
  app.get('/director/status',async c=>{
    if(!c.env.DATABASE_URL)return json(c,{configured:false})
    const sql=neon(c.env.DATABASE_URL)
    const rows=await sql`SELECT id FROM companies LIMIT 1`
    return json(c,{configured:Boolean(rows[0]),company:'TransPoli'})
  })
  app.post('/director/bootstrap',async c=>{
    const user=await currentUser(c); if(!user)return bad('Faça login como motorista/conta principal antes de configurar a diretoria.',401)
    const data=await c.req.json().catch(()=>null) as any
    if(!data)return bad('JSON inválido.',400)
    const requestedCompanyName=String(data.companyName??'').trim().slice(0,120)
    const companyName='TransPoli'
    const email=normalizeEmail(String(data.directorEmail??''))
    const pin=String(data.directorPin??'').trim()
    if(requestedCompanyName && requestedCompanyName.toLowerCase()!=='transpoli')return bad('O sistema é exclusivo da empresa TransPoli.',409)
    if(!/^\S+@\S+\.\S+$/.test(email))return bad('Informe um e-mail válido para a diretoria.',400)
    if(!/^\d{6}$/.test(pin))return bad('O PIN da diretoria deve ter 6 dígitos.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const existingCompany=await sql`SELECT id,name FROM companies LIMIT 1`
    if(existingCompany[0])return bad('A Central da Diretoria da TransPoli já foi configurada. O primeiro acesso está bloqueado.',409)
    const exists=await sql`SELECT id FROM company_members WHERE user_id=${user.id} AND status='active' LIMIT 1`
    if(exists[0])return bad('Esta conta já está vinculada a uma empresa.',409)
    const emailUsed=await sql`SELECT id FROM company_directors WHERE email=${email} LIMIT 1`
    if(emailUsed[0])return bad('Este e-mail já é usado por uma diretoria.',409)
    try{
      const pinHash=await hashSecret(pin)
      const created=await sql`INSERT INTO companies(name,created_by_user_id) VALUES(${companyName},${user.id}) RETURNING id,name`
      const company=created[0]
      if(!company)throw new Error('company_create_failed')
      await sql`INSERT INTO company_members(company_id,user_id,role) VALUES(${company.id},${user.id},'director')`
      const d=await sql`INSERT INTO company_directors(company_id,user_id,email,pin_hash) VALUES(${company.id},${user.id},${email},${pinHash}) RETURNING id,email`
      return json(c,{ok:true,company:{id:company.id,name:company.name},director:d[0]},201)
    }catch(error){
      console.error('director_bootstrap_error',error)
      return bad('Não foi possível criar a Central da Diretoria.',500)
    }
  })

  app.post('/director/login',async c=>{
    const data=await c.req.json().catch(()=>null) as any
    const email=normalizeEmail(String(data?.email??''))
    const pin=String(data?.pin??'').trim()
    if(!/^\S+@\S+\.\S+$/.test(email)||!/^\d{6}$/.test(pin))return bad('E-mail ou PIN da diretoria inválidos.',401)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`SELECT id,company_id,user_id,email,pin_hash,status FROM company_directors WHERE email=${email} LIMIT 1`
    const d=rows[0]
    if(!d||d.status!=='active'||!(await verifySecret(pin,d.pin_hash)))return bad('E-mail ou PIN da diretoria inválidos.',401)
    const token=randomToken(),h=await sha256(token),expires=new Date(Date.now()+DIRECTOR_DAYS*86400000)
    await sql`INSERT INTO company_director_sessions(director_id,token_hash,expires_at,last_seen_at) VALUES(${d.id},${h},${expires.toISOString()},NOW())`
    await sql`UPDATE company_directors SET last_login_at=NOW(),updated_at=NOW() WHERE id=${d.id}`
    return json(c,{ok:true,accessToken:token,expiresAt:expires.toISOString(),company:{id:d.company_id}})
  })

  app.post('/director/logout',async c=>{
    const token=c.req.header('Authorization')?.replace(/^Bearer\s+/i,'').trim()
    if(token){const sql=neon(c.env.DATABASE_URL!);await sql`UPDATE company_director_sessions SET revoked_at=NOW() WHERE token_hash=${await sha256(token)} AND revoked_at IS NULL`}
    return json(c,{ok:true})
  })

  app.get('/director/me',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const sql=neon(c.env.DATABASE_URL!)
    const company=await sql`SELECT id,name,status FROM companies WHERE id=${d.company_id} LIMIT 1`
    return json(c,{ok:true,director:{email:d.email},company:company[0]??null})
  })

  app.patch('/director/drivers/:id/status',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    const data=await c.req.json().catch(()=>null) as any
    const status=String(data?.status??'').trim()
    if(!/^[0-9a-fA-F-]{36}$/.test(id)||!['active','blocked'].includes(status))return bad('Status do motorista inválido.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`SELECT cm.user_id FROM company_members cm WHERE cm.company_id=${d.company_id} AND cm.user_id=${id} AND cm.role='driver' LIMIT 1`
    if(!rows[0])return bad('Motorista não pertence à TransPoli.',404)
    await sql`UPDATE users SET status=${status},updated_at=NOW() WHERE id=${id}`
    await sql`UPDATE company_members SET status=${status} WHERE company_id=${d.company_id} AND user_id=${id}`
    return json(c,{ok:true,id,status})
  })

  app.patch('/director/trucks/:id',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    const data=await c.req.json().catch(()=>null) as any
    if(!/^[0-9a-fA-F-]{36}$/.test(id)||!data)return bad('Caminhão inválido.',400)
    const truckName=String(data.truckName??'').trim().slice(0,120)
    const brand=String(data.brand??'').trim().slice(0,80)
    const model=String(data.model??'').trim().slice(0,120)
    const plate=String(data.licensePlate??'').trim().slice(0,32)
    if(!truckName&&!brand&&!model&&!plate)return bad('Informe ao menos um dado do caminhão.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`SELECT tr.id FROM trucks tr JOIN company_members cm ON cm.user_id=tr.user_id WHERE tr.id=${id} AND cm.company_id=${d.company_id} AND cm.status='active' LIMIT 1`
    if(!rows[0])return bad('Caminhão não pertence à TransPoli.',404)
    const updated=await sql`UPDATE trucks SET truck_name=${truckName||null},brand=${brand||null},model=${model||null},license_plate=${plate||null},updated_at=NOW() WHERE id=${id} RETURNING id,truck_name,brand,model,license_plate`
    return json(c,{ok:true,truck:updated[0]??null})
  })

  app.delete('/director/trucks/:id',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    if(!/^[0-9a-fA-F-]{36}$/.test(id))return bad('Caminhão inválido.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`SELECT tr.id FROM trucks tr JOIN company_members cm ON cm.user_id=tr.user_id WHERE tr.id=${id} AND cm.company_id=${d.company_id} LIMIT 1`
    if(!rows[0])return bad('Caminhão não pertence à TransPoli.',404)
    await sql`DELETE FROM trucks WHERE id=${id}`
    return json(c,{ok:true,id})
  })

  app.get('/director/dashboard',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const sql=neon(c.env.DATABASE_URL!)
    const [kpi,drivers,trucks,trips,expenses,maintenance]=await Promise.all([
      sql`SELECT
        COUNT(DISTINCT cm.user_id)::int AS drivers,
        COUNT(DISTINCT t.id) FILTER(WHERE t.status='active')::int AS active_trips,
        COUNT(DISTINCT tr.id)::int AS trucks,
        COUNT(DISTINCT t.id) FILTER(WHERE t.started_at>=date_trunc('day',NOW()))::int AS trips_today,
        COALESCE(SUM(t.distance_km) FILTER(WHERE t.status='finished'),0)::numeric AS km,
        COALESCE(SUM(t.cargo_value_brl) FILTER(WHERE t.status='finished'),0)::numeric AS revenue,
        COALESCE((SELECT SUM(e.amount) FROM expenses e JOIN company_members em ON em.user_id=e.user_id AND em.company_id=${d.company_id} AND em.status='active'),0)::numeric AS expenses
        FROM company_members cm
        LEFT JOIN trips t ON t.user_id=cm.user_id
        LEFT JOIN trucks tr ON tr.user_id=cm.user_id
        WHERE cm.company_id=${d.company_id} AND cm.status='active'`,
      sql`SELECT u.id,u.name,u.email,COUNT(t.id)::int trips,COALESCE(SUM(t.distance_km),0)::numeric km
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        LEFT JOIN trips t ON t.user_id=u.id AND t.status='finished'
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        GROUP BY u.id,u.name,u.email ORDER BY trips DESC LIMIT 100`,
      sql`SELECT tr.id,tr.truck_name,tr.brand,tr.model,tr.license_plate,
        u.name AS driver,COALESCE(SUM(t.distance_km),0)::numeric km
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        JOIN trucks tr ON tr.user_id=u.id
        LEFT JOIN trips t ON t.truck_id=tr.id AND t.status='finished'
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        GROUP BY tr.id,u.name ORDER BY tr.created_at ASC LIMIT 100`,
      sql`SELECT t.id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,t.distance_km,t.fuel_used_l,t.cargo_value_brl,t.status,u.name AS driver,tr.truck_name
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        JOIN trips t ON t.user_id=u.id
        LEFT JOIN trucks tr ON tr.id=t.truck_id
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        ORDER BY t.started_at DESC LIMIT 100`,
      sql`SELECT e.id,e.type,e.amount,e.created_at,e.trip_id,u.name AS driver
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        JOIN expenses e ON e.user_id=u.id
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        ORDER BY e.created_at DESC LIMIT 100`,
      sql`SELECT m.id,m.truck_id,m.service_type,m.cost,m.created_at,tr.truck_name,u.name AS driver
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        JOIN maintenance m ON m.user_id=u.id
        LEFT JOIN trucks tr ON tr.id=m.truck_id
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        ORDER BY m.created_at DESC LIMIT 100`
    ])
    const x=kpi[0]??{}
    const revenue=Number(x.revenue||0), expenseTotal=Number(x.expenses||0)
    return json(c,{ok:true,updatedAt:new Date().toISOString(),kpis:{
      drivers:Number(x.drivers||0),trucks:Number(x.trucks||0),activeTrips:Number(x.active_trips||0),
      tripsToday:Number(x.trips_today||0),km:Number(x.km||0),revenue:Number(revenue.toFixed(2)),
      expenses:Number(expenseTotal.toFixed(2)),result:Number((revenue-expenseTotal).toFixed(2))
    },drivers,trucks,trips,expenses,maintenance})
  })
}
