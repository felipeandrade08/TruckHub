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

  const legacy=await sql`SELECT d.id AS director_id,d.company_id,d.user_id,d.email,d.status,
      co.name AS company_name
    FROM company_director_sessions s
    JOIN company_directors d ON d.id=s.director_id
    JOIN companies co ON co.id=d.company_id
    WHERE s.token_hash=${h} AND s.revoked_at IS NULL AND s.expires_at>NOW()
      AND d.status='active' AND co.status='active' LIMIT 1`
  if(legacy[0]){
    await sql`UPDATE company_director_sessions SET last_seen_at=NOW() WHERE token_hash=${h} AND revoked_at IS NULL`
    return legacy[0]
  }

  const account=await sql`SELECT cm.company_id,cm.user_id,u.email,cm.status,co.name AS company_name,
      cm.role,NULL::uuid AS director_id
    FROM sessions s
    JOIN users u ON u.id=s.user_id
    JOIN company_members cm ON cm.user_id=u.id
    JOIN companies co ON co.id=cm.company_id
    WHERE s.token_hash=${h} AND s.revoked_at IS NULL AND s.expires_at>NOW()
      AND s.session_type IN ('web','desktop') AND u.status='active'
      AND cm.status='active' AND cm.role IN ('director','manager') AND co.status='active'
    ORDER BY CASE cm.role WHEN 'director' THEN 0 ELSE 1 END LIMIT 1`
  if(!account[0])return null
  await sql`UPDATE sessions SET last_seen_at=NOW() WHERE token_hash=${h} AND revoked_at IS NULL`
  return account[0]
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
    // O primeiro acesso pode ser feito pela conta principal que já existe no sistema.
    // Ela autoriza somente a criação inicial; o acesso diário da diretoria usa e-mail + PIN.
    const emailUsed=await sql`SELECT id FROM company_directors WHERE email=${email} LIMIT 1`
    if(emailUsed[0])return bad('Este e-mail já é usado por uma diretoria.',409)
    try{
      const pinHash=await hashSecret(pin)
      const created=await sql`INSERT INTO companies(name,created_by_user_id) VALUES(${companyName},${user.id}) RETURNING id,name`
      const company=created[0]
      if(!company)throw new Error('company_create_failed')
      await sql`INSERT INTO company_members(company_id,user_id,role,status) VALUES(${company.id},${user.id},'director','active')`
      await sql`INSERT INTO company_members(company_id,user_id,role,status)
        SELECT ${company.id},u.id,'driver','active'
        FROM users u
        WHERE u.status='active'
          AND u.id<>${user.id}
          AND NOT EXISTS (
            SELECT 1 FROM company_members existing_member
            WHERE existing_member.company_id=${company.id} AND existing_member.user_id=u.id
          )
        ON CONFLICT (company_id,user_id) DO NOTHING`
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

  app.get('/me/company-employment',async c=>{
    const u=await currentUser(c); if(!u)return bad('Sessão inválida ou expirada.',401)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`SELECT cm.company_id,co.name AS company_name,cm.role,cm.status,cm.employment_type,
      cm.registration_number,cm.badge_issued_at,cm.joined_at,
      p.aggregate_driver_share,p.company_driver_share,p.aggregate_fuel_payer,p.aggregate_maintenance_payer,
      p.company_driver_fuel_payer,p.company_driver_maintenance_payer
      FROM company_members cm JOIN companies co ON co.id=cm.company_id
      LEFT JOIN company_financial_policy p ON p.company_id=cm.company_id
      WHERE cm.user_id=${u.id} AND cm.status='active' AND co.status='active' LIMIT 1`
    return json(c,{ok:true,employment:rows[0]??null})
  })

  app.post('/me/company-employment/select',async c=>{
    const u=await currentUser(c); if(!u)return bad('Sessão inválida ou expirada.',401)
    const data=await c.req.json().catch(()=>null) as any
    const type=String(data?.employmentType??'')
    if(!['aggregate','company_driver'].includes(type))return bad('Escolha Agregado ou Motorista da Empresa.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const member=await sql`SELECT cm.company_id,cm.employment_type
      FROM company_members cm JOIN companies co ON co.id=cm.company_id
      WHERE cm.user_id=${u.id} AND cm.role='driver' AND cm.status='active' AND co.status='active' LIMIT 1`
    if(!member[0])return bad('Motorista não está vinculado a uma empresa ativa.',404)
    if(member[0].employment_type&&member[0].employment_type!=='pending')
      return bad('A modalidade profissional já foi escolhida. Alterações posteriores devem passar pela Diretoria.',409)
    const activeTrip=await sql`SELECT id FROM trips WHERE user_id=${u.id} AND status='active' LIMIT 1`
    if(activeTrip[0])return bad('Finalize a viagem ativa antes de definir a modalidade profissional.',409)
    const companyId=member[0].company_id
    const rows=await sql`UPDATE company_members SET employment_type=${type},employment_selected_at=NOW(),
      registration_number=COALESCE(registration_number,'TP-DRV-'||LPAD(nextval('company_driver_registration_seq')::text,6,'0')),
      badge_issued_at=COALESCE(badge_issued_at,NOW())
      WHERE company_id=${companyId} AND user_id=${u.id}
      RETURNING company_id,role,status,employment_type,registration_number,badge_issued_at,joined_at`
    return json(c,{ok:true,employment:rows[0]??null})
  })

  app.post('/me/company-loans',async c=>{
    const u=await currentUser(c); if(!u)return bad('Sessão inválida ou expirada.',401)
    const data=await c.req.json().catch(()=>null) as any
    const principal=Number(data?.principal)
    if(!Number.isFinite(principal)||principal<1000||principal>100000)return bad('Solicite um valor entre R$ 1.000 e R$ 100.000.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const member=await sql`SELECT cm.company_id,p.loan_interest_rate,p.loan_repayment_percent
      FROM company_members cm JOIN companies co ON co.id=cm.company_id
      JOIN company_financial_policy p ON p.company_id=cm.company_id
      WHERE cm.user_id=${u.id} AND cm.role='driver' AND cm.status='active' AND co.status='active'
        AND cm.employment_type IN ('aggregate','company_driver') LIMIT 1`
    if(!member[0])return bad('Escolha sua modalidade profissional antes de solicitar crédito.',409)
    const open=await sql`SELECT id FROM company_loans WHERE company_id=${member[0].company_id} AND user_id=${u.id}
      AND status IN ('pending','approved','active') LIMIT 1`
    if(open[0])return bad('Você já possui uma solicitação ou empréstimo empresarial em aberto.',409)
    const interest=Number(member[0].loan_interest_rate||0),total=Number((principal*(1+interest/100)).toFixed(2))
    const rows=await sql`INSERT INTO company_loans(company_id,user_id,principal,interest_rate,total_due,repayment_percent,status)
      VALUES(${member[0].company_id},${u.id},${principal},${interest},${total},${member[0].loan_repayment_percent},'pending')
      RETURNING *`
    return json(c,{ok:true,loan:rows[0]},201)
  })

  app.get('/me/company-loans',async c=>{
    const u=await currentUser(c); if(!u)return bad('Sessão inválida ou expirada.',401)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`SELECT cl.* FROM company_loans cl JOIN company_members cm ON cm.company_id=cl.company_id AND cm.user_id=cl.user_id
      WHERE cl.user_id=${u.id} ORDER BY cl.requested_at DESC LIMIT 20`
    return json(c,{ok:true,loans:rows})
  })

  app.post('/director/company-loans/:id/decision',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??''),data=await c.req.json().catch(()=>null) as any,decision=String(data?.decision??'')
    if(!/^[0-9a-fA-F-]{36}$/.test(id)||!['approve','reject'].includes(decision))return bad('Decisão de crédito inválida.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`SELECT * FROM company_loans WHERE id=${id} AND company_id=${d.company_id} LIMIT 1`,loan=rows[0]
    if(!loan)return bad('Empréstimo não encontrado.',404)
    if(loan.status!=='pending')return bad('Esta solicitação já foi analisada.',409)
    if(decision==='reject'){
      const rejected=await sql`UPDATE company_loans SET status='rejected',closed_at=NOW() WHERE id=${id} RETURNING *`
      return json(c,{ok:true,loan:rejected[0]})
    }
    try{
      const approved=await sql`SELECT * FROM approve_company_loan(${id},${d.company_id})`
      const result=approved[0]
      if(!result)return bad('Não foi possível liberar o empréstimo.',500)
      return json(c,{ok:true,loan:result.loan,driverBalance:Number(result.driver_balance||0)})
    }catch(error){
      const message=error instanceof Error?error.message:''
      if(message.includes('company_balance_insufficient'))return bad('Saldo empresarial insuficiente para liberar este empréstimo.',409)
      if(message.includes('loan_already_decided'))return bad('Esta solicitação já foi analisada.',409)
      console.error('company_loan_approval_error',error);return bad('Não foi possível liberar o empréstimo.',500)
    }

  })

  app.get('/director/company-economy',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const sql=neon(c.env.DATABASE_URL!)
    const [policy,balance,recent,loans]=await Promise.all([
      sql`SELECT aggregate_driver_share,company_driver_share,aggregate_fuel_payer,aggregate_maintenance_payer,
        company_driver_fuel_payer,company_driver_maintenance_payer,loan_interest_rate,loan_repayment_percent
        FROM company_financial_policy WHERE company_id=${d.company_id} LIMIT 1`,
      sql`SELECT COALESCE(SUM(amount),0)::numeric AS balance FROM company_ledger WHERE company_id=${d.company_id}`,
      sql`SELECT l.id,l.type,l.amount,l.note,l.created_at,u.name AS driver_name
        FROM company_ledger l LEFT JOIN users u ON u.id=l.user_id
        WHERE l.company_id=${d.company_id} ORDER BY l.created_at DESC LIMIT 40`,
      sql`SELECT cl.id,cl.user_id,u.name AS driver_name,cl.principal,cl.interest_rate,cl.total_due,cl.paid_amount,
        cl.repayment_percent,cl.status,cl.requested_at,cl.approved_at
        FROM company_loans cl JOIN users u ON u.id=cl.user_id
        WHERE cl.company_id=${d.company_id} ORDER BY cl.requested_at DESC LIMIT 40`
    ])
    return json(c,{ok:true,balance:Number(balance[0]?.balance||0),policy:policy[0]??null,recent,loans})
  })

  app.patch('/director/company-policy',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const data=await c.req.json().catch(()=>null) as any
    const aggregate=Number(data?.aggregateDriverShare),employee=Number(data?.companyDriverShare)
    const interest=Number(data?.loanInterestRate),repayment=Number(data?.loanRepaymentPercent)
    if(![aggregate,employee,interest,repayment].every(Number.isFinite)||
       aggregate<0||aggregate>100||employee<0||employee>100||interest<0||interest>100||repayment<0||repayment>100)
      return bad('Política financeira inválida.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`INSERT INTO company_financial_policy(company_id,aggregate_driver_share,company_driver_share,loan_interest_rate,loan_repayment_percent)
      VALUES(${d.company_id},${aggregate},${employee},${interest},${repayment})
      ON CONFLICT(company_id) DO UPDATE SET aggregate_driver_share=EXCLUDED.aggregate_driver_share,
        company_driver_share=EXCLUDED.company_driver_share,loan_interest_rate=EXCLUDED.loan_interest_rate,
        loan_repayment_percent=EXCLUDED.loan_repayment_percent,updated_at=NOW()
      RETURNING *`
    return json(c,{ok:true,policy:rows[0]})
  })

  app.post('/director/drivers',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const data=await c.req.json().catch(()=>null) as any
    const name=String(data?.name??'').trim().slice(0,120)
    const email=normalizeEmail(String(data?.email??''))
    const password=String(data?.password??'')
    const pin=String(data?.pin??'').trim()
    if(name.length<2||!/^\S+@\S+\.\S+$/.test(email)||password.length<8||!/^\d{6}$/.test(pin))
      return bad('Informe nome, e-mail, senha de pelo menos 8 caracteres e PIN de 6 dígitos.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const exists=await sql`SELECT id FROM users WHERE email=${email} LIMIT 1`
    if(exists[0])return bad('Este e-mail já possui uma conta.',409)
    try{
      const passwordHash=await hashSecret(password),pinHash=await hashSecret(pin)
      const rows=await sql`WITH new_user AS (
        INSERT INTO users(name,email,password_hash,pin_hash,status)
        VALUES(${name},${email},${passwordHash},${pinHash},'active')
        RETURNING id,name,email,status,created_at
      ), new_license AS (
        INSERT INTO licenses(user_id,license_type,status,trial_started_at,trial_expires_at,activated_at)
        SELECT id,'lifetime','active',created_at,created_at+INTERVAL '7 days',created_at FROM new_user
      )
      SELECT id,name,email,status,created_at FROM new_user`
      const user=rows[0]; if(!user)throw new Error('driver_create_failed')
      await sql`INSERT INTO company_members(company_id,user_id,role,status) VALUES(${d.company_id},${user.id},'driver','active')`
      return json(c,{ok:true,driver:user},201)
    }catch(error){console.error('director_driver_create_error',error);return bad('Não foi possível cadastrar o motorista.',500)}
  })

  app.patch('/director/drivers/:id',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    const data=await c.req.json().catch(()=>null) as any
    const name=String(data?.name??'').trim().slice(0,120)
    const email=normalizeEmail(String(data?.email??''))
    const password=String(data?.password??'')
    const pin=String(data?.pin??'').trim()
    const licenseStatus=String(data?.licenseStatus??'').trim()
    if(!/^[0-9a-fA-F-]{36}$/.test(id)||name.length<2||!/^\S+@\S+\.\S+$/.test(email))
      return bad('Dados do motorista inválidos.',400)
    if(password && password.length<8)return bad('A nova senha deve ter pelo menos 8 caracteres.',400)
    if(pin && !/^\d{6}$/.test(pin))return bad('O PIN deve ter 6 dígitos.',400)
    if(licenseStatus && !['trial','active','expired','blocked'].includes(licenseStatus))return bad('Situação da licença inválida.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const member=await sql`SELECT u.id FROM users u JOIN company_members cm ON cm.user_id=u.id WHERE u.id=${id} AND cm.company_id=${d.company_id} AND cm.role='driver' LIMIT 1`
    if(!member[0])return bad('Motorista não pertence à TransPoli.',404)
    const duplicate=await sql`SELECT id FROM users WHERE email=${email} AND id<>${id} LIMIT 1`
    if(duplicate[0])return bad('Este e-mail já pertence a outra conta.',409)
    const passwordHash=password?await hashSecret(password):null
    const pinHash=pin?await hashSecret(pin):null
    const updated=await sql`UPDATE users SET name=${name},email=${email},
      password_hash=COALESCE(${passwordHash},password_hash),pin_hash=COALESCE(${pinHash},pin_hash),updated_at=NOW()
      WHERE id=${id} RETURNING id,name,email,status,updated_at`
    if(licenseStatus)await sql`UPDATE licenses SET status=${licenseStatus},updated_at=NOW() WHERE user_id=${id}`
    return json(c,{ok:true,driver:updated[0]??null})
  })

  app.delete('/director/drivers/:id/link',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    if(!/^[0-9a-fA-F-]{36}$/.test(id))return bad('Motorista inválido.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const member=await sql`SELECT user_id FROM company_members WHERE company_id=${d.company_id} AND user_id=${id} AND role='driver' LIMIT 1`
    if(!member[0])return bad('Motorista não está vinculado à TransPoli.',404)
    await sql`UPDATE company_members SET status='blocked' WHERE company_id=${d.company_id} AND user_id=${id}`
    return json(c,{ok:true,id,status:'unlinked'})
  })

  app.post('/director/drivers/:id/link',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    if(!/^[0-9a-fA-F-]{36}$/.test(id))return bad('Motorista inválido.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const member=await sql`SELECT user_id FROM company_members WHERE company_id=${d.company_id} AND user_id=${id} AND role='driver' LIMIT 1`
    if(member[0]){
      await sql`UPDATE company_members SET status='active' WHERE company_id=${d.company_id} AND user_id=${id}`
      await sql`UPDATE users SET status='active',updated_at=NOW() WHERE id=${id}`
      return json(c,{ok:true,id,status:'linked'})
    }
    const user=await sql`SELECT id FROM users WHERE id=${id} LIMIT 1`
    if(!user[0])return bad('Conta do motorista não encontrada.',404)
    await sql`INSERT INTO company_members(company_id,user_id,role,status) VALUES(${d.company_id},${id},'driver','active')`
    await sql`UPDATE users SET status='active',updated_at=NOW() WHERE id=${id}`
    return json(c,{ok:true,id,status:'linked'})
  })

  app.get('/director/drivers/:id/history',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    if(!/^[0-9a-fA-F-]{36}$/.test(id))return bad('Motorista inválido.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const member=await sql`SELECT 1 FROM company_members WHERE company_id=${d.company_id} AND user_id=${id} AND role='driver' LIMIT 1`
    if(!member[0])return bad('Motorista não pertence à TransPoli.',404)
    const [driver,trips,events]=await Promise.all([
      sql`SELECT u.id,u.name,u.email,u.status,cm.status AS membership_status,
        l.status AS license_status,l.license_type,l.trial_expires_at,l.expires_at
        FROM users u JOIN company_members cm ON cm.user_id=u.id
        LEFT JOIN licenses l ON l.user_id=u.id
        WHERE u.id=${id} AND cm.company_id=${d.company_id} LIMIT 1`,
      sql`SELECT t.id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,t.distance_km,t.fuel_used_l,t.cargo_value_brl,t.status,tr.truck_name
        FROM trips t LEFT JOIN trucks tr ON tr.id=t.truck_id
        WHERE t.user_id=${id} ORDER BY t.started_at DESC LIMIT 50`,
      sql`SELECT id,event_type,event_at,payload FROM transpoli_operational_events
        WHERE user_id=${id} ORDER BY event_at DESC LIMIT 50`
    ])
    return json(c,{ok:true,driver:driver[0]??null,trips,events})
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

  app.post('/director/trucks',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const data=await c.req.json().catch(()=>null) as any
    const userId=String(data?.userId??'')
    const truckName=String(data?.truckName??'').trim().slice(0,120)
    const brand=String(data?.brand??'').trim().slice(0,80)
    const model=String(data?.model??'').trim().slice(0,120)
    const plate=String(data?.licensePlate??'').trim().slice(0,32)
    if(!/^[0-9a-fA-F-]{36}$/.test(userId)||(!truckName&&!brand&&!model&&!plate))return bad('Informe o motorista e os dados do caminhão.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const member=await sql`SELECT user_id FROM company_members WHERE company_id=${d.company_id} AND user_id=${userId} AND role='driver' AND status='active' LIMIT 1`
    if(!member[0])return bad('Motorista não pertence à TransPoli.',404)
    const created=await sql`INSERT INTO trucks(user_id,truck_name,brand,model,license_plate) VALUES(${userId},${truckName||null},${brand||null},${model||null},${plate||null}) RETURNING id,truck_name,brand,model,license_plate`
    return json(c,{ok:true,truck:created[0]},201)
  })

  app.patch('/director/trucks/:id',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    const data=await c.req.json().catch(()=>null) as any
    if(!/^[0-9a-fA-F-]{36}$/.test(id)||!data)return bad('Caminhão inválido.',400)
    const userId=String(data.userId??'')
    const truckName=String(data.truckName??'').trim().slice(0,120)
    const brand=String(data.brand??'').trim().slice(0,80)
    const model=String(data.model??'').trim().slice(0,120)
    const plate=String(data.licensePlate??'').trim().slice(0,32)
    const operationalState=String(data.operationalState??'').trim()
    if(!truckName&&!brand&&!model&&!plate)return bad('Informe ao menos um dado do caminhão.',400)
    if(userId && !/^[0-9a-fA-F-]{36}$/.test(userId))return bad('Motorista inválido.',400)
    if(operationalState && !['normal','paused','maintenance','offline'].includes(operationalState))return bad('Situação operacional inválida.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const rows=await sql`SELECT tr.id,tr.user_id FROM trucks tr JOIN company_members cm ON cm.user_id=tr.user_id WHERE tr.id=${id} AND cm.company_id=${d.company_id} LIMIT 1`
    if(!rows[0])return bad('Caminhão não pertence à TransPoli.',404)
    if(userId){
      const member=await sql`SELECT user_id FROM company_members WHERE company_id=${d.company_id} AND user_id=${userId} AND role='driver' AND status='active' LIMIT 1`
      if(!member[0])return bad('O novo motorista não pertence à TransPoli ou está inativo.',400)
    }
    const updated=await sql`UPDATE trucks SET user_id=${userId||rows[0].user_id},truck_name=${truckName||null},brand=${brand||null},model=${model||null},license_plate=${plate||null},operational_state=COALESCE(NULLIF(${operationalState},''),operational_state),updated_at=NOW() WHERE id=${id} RETURNING id,user_id,truck_name,brand,model,license_plate,operational_state,current_odometer_km,current_fuel_l,wear_pct,last_telemetry_at,last_maintenance_at`
    return json(c,{ok:true,truck:updated[0]??null})
  })

  app.get('/director/trucks/:id/history',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    if(!/^[0-9a-fA-F-]{36}$/.test(id))return bad('Caminhão inválido.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const truck=await sql`SELECT tr.id,tr.user_id,tr.truck_name,tr.brand,tr.model,tr.license_plate,tr.operational_state,tr.current_odometer_km,tr.current_fuel_l,tr.wear_pct,tr.last_telemetry_at,tr.last_maintenance_at,u.name AS driver
      FROM trucks tr JOIN company_members cm ON cm.user_id=tr.user_id JOIN users u ON u.id=tr.user_id
      WHERE tr.id=${id} AND cm.company_id=${d.company_id} LIMIT 1`
    if(!truck[0])return bad('Caminhão não pertence à TransPoli.',404)
    const [trips,maintenance]=await Promise.all([
      sql`SELECT t.id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,t.distance_km,t.fuel_used_l,t.cargo_value_brl,t.status
        FROM trips t WHERE t.truck_id=${id} ORDER BY t.started_at DESC LIMIT 100`,
      sql`SELECT id,service_type,component,description,cost_brl,odometer_km,wear_engine,wear_transmission,wear_cabin,wear_chassis,wear_wheels,created_at
        FROM truck_maintenance_records WHERE truck_id=${id} ORDER BY created_at DESC LIMIT 100`
    ])
    return json(c,{ok:true,truck:truck[0],trips,maintenance})
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

  app.get('/director/trips/:id',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const id=String(c.req.param('id')??'')
    if(!/^[0-9a-fA-F-]{36}$/.test(id))return bad('Viagem inválida.',400)
    const sql=neon(c.env.DATABASE_URL!)
    const trip=await sql`SELECT t.id,t.user_id,t.truck_id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,
      t.distance_km,t.fuel_used_l,t.cargo_value_brl,t.status,t.cargo_damage,t.cargo_mass_kg,
      t.planned_distance_km,t.start_odometer_km,t.end_odometer_km,t.start_fuel_l,t.end_fuel_l,
      u.name AS driver,u.email AS driver_email,tr.truck_name,tr.brand,tr.model,tr.license_plate
      FROM trips t JOIN company_members cm ON cm.user_id=t.user_id
      JOIN users u ON u.id=t.user_id
      LEFT JOIN trucks tr ON tr.id=t.truck_id
      WHERE t.id=${id} AND cm.company_id=${d.company_id} LIMIT 1`
    if(!trip[0])return bad('Viagem não pertence à TransPoli.',404)
    const [expenses,telemetry,events]=await Promise.all([
      sql`SELECT id,type,description,amount,created_at FROM expenses WHERE trip_id=${id} AND user_id=${trip[0].user_id} ORDER BY created_at DESC LIMIT 100`,
      sql`SELECT recorded_at,speed_kph,rpm,gear,fuel_l,odometer_km,fuel_range_km,game_paused FROM trip_telemetry_samples WHERE trip_id=${id} ORDER BY recorded_at DESC LIMIT 200`,
      sql`SELECT id,event_type,event_at,payload FROM transpoli_operational_events WHERE trip_id=${id} ORDER BY event_at DESC LIMIT 100`
    ])
    const expenseTotal=expenses.reduce((s:any,e:any)=>s+Number(e.amount||0),0)
    const settlement=await sql`SELECT gross_revenue,company_share,driver_gross,driver_expenses,company_expenses,loan_payment,driver_net,employment_type,settled_at
      FROM company_trip_settlements WHERE trip_id=${id} AND company_id=${d.company_id} LIMIT 1`
    return json(c,{ok:true,trip:trip[0],expenses,telemetry,events,financial:settlement[0]??{
      gross_revenue:null,company_share:null,driver_gross:null,driver_expenses:Number(expenseTotal.toFixed(2)),
      company_expenses:null,loan_payment:null,driver_net:null,employment_type:null,settled_at:null
    }})
  })

  app.get('/director/dashboard',async c=>{
    const d=await director(c); if(!d)return bad('Sessão da diretoria inválida ou expirada.',401)
    const sql=neon(c.env.DATABASE_URL!)
    // Compatibilidade: usuários criados antes da Central da Diretoria também passam a pertencer à TransPoli.
    // A conta que criou a empresa e qualquer diretor já cadastrado permanecem fora da lista de motoristas.
    await sql`INSERT INTO company_members(company_id,user_id,role,status)
      SELECT co.id,u.id,'driver','active'
      FROM companies co CROSS JOIN users u
      WHERE co.id=${d.company_id}
        AND u.status='active'
        AND u.id<>co.created_by_user_id
        AND NOT EXISTS (
          SELECT 1 FROM company_directors existing_director
          WHERE existing_director.company_id=co.id AND existing_director.user_id=u.id
        )
        AND NOT EXISTS (
          SELECT 1 FROM company_members existing_member
          WHERE existing_member.company_id=co.id AND existing_member.user_id=u.id
        )
      ON CONFLICT (company_id,user_id) DO NOTHING`
    const [kpi,drivers,trucks,trips,expenses,maintenance,bankRecent,companyLoans]=await Promise.all([
      sql`SELECT
        (SELECT COUNT(*) FROM company_members cm JOIN users u ON u.id=cm.user_id WHERE cm.company_id=${d.company_id} AND cm.role='driver' AND cm.status='active' AND u.status='active')::int AS drivers,
        (SELECT COUNT(DISTINCT tr.id) FROM trucks tr JOIN company_members cm ON cm.user_id=tr.user_id WHERE cm.company_id=${d.company_id} AND cm.status='active')::int AS trucks,
        (SELECT COUNT(DISTINCT cm.user_id) FROM company_members cm JOIN trucks tr ON tr.user_id=cm.user_id WHERE cm.company_id=${d.company_id} AND cm.role='driver' AND cm.status='active' AND tr.last_telemetry_at>=NOW()-INTERVAL '45 seconds')::int AS drivers_online,
        (SELECT COUNT(*) FROM trips t JOIN company_members cm ON cm.user_id=t.user_id WHERE cm.company_id=${d.company_id} AND cm.status='active' AND t.status='active')::int AS active_trips,
        (SELECT COUNT(*) FROM trips t JOIN company_members cm ON cm.user_id=t.user_id WHERE cm.company_id=${d.company_id} AND cm.status='active' AND t.status='finished' AND t.finished_at>=date_trunc('day',NOW()))::int AS completed_today,
        COALESCE((SELECT SUM(t.distance_km) FROM trips t JOIN company_members cm ON cm.user_id=t.user_id WHERE cm.company_id=${d.company_id} AND cm.status='active' AND t.status='finished' AND t.finished_at>=date_trunc('day',NOW())),0)::numeric AS km_today,
        COALESCE((SELECT SUM(s.company_share) FROM company_trip_settlements s WHERE s.company_id=${d.company_id}),0)::numeric AS revenue,
        COALESCE((SELECT SUM(s.company_share) FROM company_trip_settlements s WHERE s.company_id=${d.company_id} AND s.settled_at>=date_trunc('day',NOW())),0)::numeric AS revenue_today,
        COALESCE(-(SELECT SUM(l.amount) FROM company_ledger l WHERE l.company_id=${d.company_id} AND l.amount<0),0)::numeric AS expenses,
        COALESCE(-(SELECT SUM(l.amount) FROM company_ledger l WHERE l.company_id=${d.company_id} AND l.amount<0 AND l.created_at>=date_trunc('day',NOW())),0)::numeric AS expenses_today,
        COALESCE((SELECT SUM(l.amount) FROM company_ledger l WHERE l.company_id=${d.company_id}),0)::numeric AS company_balance`,
      sql`SELECT u.id,u.name,u.email,u.status,cm.status AS membership_status,cm.employment_type,cm.registration_number,
        l.status AS license_status,l.license_type,l.trial_expires_at,l.expires_at,
        COALESCE(stats.trips,0)::int AS trips,COALESCE(stats.km,0)::numeric AS km,
        live.recorded_at AS live_at,
        CASE WHEN live.recorded_at>=NOW()-INTERVAL '90 seconds' AND live.connected=TRUE THEN 'online' ELSE 'offline' END AS presence,
        COALESCE(NULLIF(CONCAT_WS(' ',live.truck_brand,live.truck_model),''),'—') AS live_truck,
        COALESCE(live.cargo,'Sem carga') AS live_cargo,
        COALESCE(live.source_city,'—') AS live_origin,COALESCE(live.destination_city,'—') AS live_destination,
        COALESCE(live.speed_kph,0)::numeric AS live_speed_kph,
        CASE WHEN live.refuel_active THEN 'ABASTECENDO' WHEN live.game_paused THEN 'PAUSADO' WHEN live.on_job THEN 'EM VIAGEM' WHEN live.recorded_at>=NOW()-INTERVAL '90 seconds' AND live.connected=TRUE THEN 'DISPONÍVEL' ELSE 'OFFLINE' END AS operation_status
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        LEFT JOIN licenses l ON l.user_id=u.id
        LEFT JOIN LATERAL (SELECT COUNT(*)::int trips,COALESCE(SUM(t.distance_km),0)::numeric km FROM trips t WHERE t.user_id=u.id AND t.status='finished') stats ON TRUE
        LEFT JOIN device_telemetry_latest live ON live.user_id=u.id
        WHERE cm.company_id=${d.company_id} AND cm.status IN ('active','blocked') AND cm.role='driver'
        ORDER BY presence DESC,live.recorded_at DESC NULLS LAST,u.name ASC LIMIT 100`,
      sql`SELECT tr.id,tr.user_id,tr.truck_name,tr.brand,tr.model,tr.license_plate,
        tr.operational_state,tr.current_odometer_km,tr.current_fuel_l,tr.wear_pct,tr.last_telemetry_at,tr.last_maintenance_at,
        u.name AS driver,COALESCE(SUM(t.distance_km),0)::numeric km,
        CASE
          WHEN tr.last_telemetry_at IS NULL OR tr.last_telemetry_at<NOW()-INTERVAL '10 minutes' THEN 'OFFLINE'
          WHEN COALESCE(tr.wear_pct,0)>=75 THEN 'DESGASTE CRÍTICO'
          WHEN COALESCE(tr.wear_pct,0)>=50 THEN 'MANUTENÇÃO RECOMENDADA'
          WHEN COALESCE(tr.current_fuel_l,0)<=20 THEN 'COMBUSTÍVEL BAIXO'
          ELSE 'NORMAL'
        END AS fleet_alert
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        JOIN trucks tr ON tr.user_id=u.id
        LEFT JOIN trips t ON t.truck_id=tr.id AND t.status='finished'
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        GROUP BY tr.id,u.name ORDER BY
          CASE WHEN COALESCE(tr.wear_pct,0)>=75 THEN 0 WHEN COALESCE(tr.wear_pct,0)>=50 THEN 1 WHEN tr.last_telemetry_at IS NULL OR tr.last_telemetry_at<NOW()-INTERVAL '10 minutes' THEN 2 ELSE 3 END,
          tr.created_at ASC LIMIT 100`,
      sql`SELECT t.id,t.cargo,t.origin,t.destination,t.started_at,t.finished_at,t.distance_km,t.fuel_used_l,t.status,
        s.gross_revenue AS trip_revenue_brl,s.company_share AS company_share_brl,s.driver_gross AS driver_gross_brl,
        s.driver_expenses AS expenses_brl,s.loan_payment AS loan_payment_brl,s.driver_net AS driver_net_brl,
        u.name AS driver,tr.truck_name
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        JOIN trips t ON t.user_id=u.id
        LEFT JOIN trucks tr ON tr.id=t.truck_id
        LEFT JOIN company_trip_settlements s ON s.trip_id=t.id AND s.company_id=cm.company_id
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        ORDER BY t.started_at DESC LIMIT 100`,
      sql`SELECT e.id,e.type,e.amount,e.created_at,e.trip_id,u.name AS driver
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        JOIN expenses e ON e.user_id=u.id
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        ORDER BY e.created_at DESC LIMIT 100`,
      sql`SELECT m.id,m.truck_id,m.service_type,m.cost_brl AS cost,m.created_at,tr.truck_name,u.name AS driver
        FROM company_members cm JOIN users u ON u.id=cm.user_id
        JOIN truck_maintenance_records m ON m.user_id=u.id
        LEFT JOIN trucks tr ON tr.id=m.truck_id
        WHERE cm.company_id=${d.company_id} AND cm.status='active'
        ORDER BY m.created_at DESC LIMIT 100`,
      sql`SELECT l.id,l.type,l.amount,l.note,l.created_at,u.name AS driver_name
        FROM company_ledger l LEFT JOIN users u ON u.id=l.user_id
        WHERE l.company_id=${d.company_id} ORDER BY l.created_at DESC LIMIT 40`,
      sql`SELECT cl.id,cl.user_id,cl.principal,cl.interest_rate,cl.total_due,cl.paid_amount,cl.status,cl.created_at,u.name AS driver_name
        FROM company_loans cl JOIN users u ON u.id=cl.user_id
        WHERE cl.company_id=${d.company_id} ORDER BY CASE WHEN cl.status='pending' THEN 0 WHEN cl.status='active' THEN 1 ELSE 2 END,cl.created_at DESC LIMIT 40`
    ])
    const x=kpi[0]??{}
    const revenue=Number(x.revenue||0), expenseTotal=Number(x.expenses||0)
    return json(c,{ok:true,updatedAt:new Date().toISOString(),kpis:{
      drivers:Number(x.drivers||0),driversOnline:Number(x.drivers_online||0),trucks:Number(x.trucks||0),activeTrips:Number(x.active_trips||0),
      completedToday:Number(x.completed_today||0),kmToday:Number(x.km_today||0),
      revenueToday:Number(x.revenue_today||0),expensesToday:Number(x.expenses_today||0),
      revenue:Number(x.revenue||0),expenses:Number(x.expenses||0),companyBalance:Number(x.company_balance||0),
      result:Number((Number(x.revenue||0)-Number(x.expenses||0)).toFixed(2)),
      resultToday:Number((Number(x.revenue_today||0)-Number(x.expenses_today||0)).toFixed(2))
    },drivers,trucks,trips,expenses,maintenance,companyEconomy:{balance:Number(x.company_balance||0),recent:bankRecent,loans:companyLoans}})
  })
}
