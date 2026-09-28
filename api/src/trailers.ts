import { neon } from '@neondatabase/serverless'
import { hashSessionToken, getCookie } from './sharedAuth'

function clean(v:any,max=160){return String(v??'').trim().slice(0,max)}
function key(brand:any,model:any,plate:any,name:any){
  const norm=(v:any)=>String(v??'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').trim().toLowerCase().replace(/\s+/g,' ')
  return [brand,model,plate,name].map(norm).join('|')
}
async function currentUser(c:any){
  const bearer=c.req.header('Authorization')?.replace(/^Bearer\s+/i,'').trim()
  const token=bearer||getCookie(c.req.raw,'truckhub_session')
  if(!token||!c.env.DATABASE_URL)return null
  const sql=neon(c.env.DATABASE_URL)
  const rows=await sql`SELECT u.id,u.name,u.email FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${await hashSessionToken(token)} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND u.status='active' LIMIT 1`
  return rows[0]??null
}
export function registerTrailerRoutes(app:any){
  const unauthorized=(c:any)=>c.json({ok:false,error:'Sessão inválida ou expirada.'},401)
  app.get('/me/garage/trailers',async(c:any)=>{
    const user=await currentUser(c);if(!user)return unauthorized(c)
    try{
      const sql=neon(c.env.DATABASE_URL)
      const rows=await sql`SELECT id,trailer_key,trailer_name,brand,model,license_plate,profile_name,owned_from_save,active,created_at,updated_at FROM garage_trailers WHERE user_id=${user.id} AND active=TRUE ORDER BY trailer_name ASC,created_at DESC`
      return c.json({ok:true,trailers:rows},{headers:{'Cache-Control':'no-store'}})
    }catch(error){console.error('garage_trailers_list_error',error);return c.json({ok:false,error:'Erro ao carregar os reboques.'},500)}
  })
  app.post('/me/garage/trailers/sync',async(c:any)=>{
    const user=await currentUser(c);if(!user)return unauthorized(c)
    try{
      const body=await c.req.json().catch(()=>null) as any
      const trailers=Array.isArray(body?.trailers)?body.trailers.slice(0,200):[]
      const sql=neon(c.env.DATABASE_URL)
      const incoming:string[]=[]
      for(const item of trailers){
        const brand=clean(item?.brand,80),model=clean(item?.model,120),plate=clean(item?.plate,32),name=clean(item?.name,160)||[brand,model].filter(Boolean).join(' ')||'Reboque'
        const trailerKey=clean(item?.key,220)||key(brand,model,plate,name)
        incoming.push(trailerKey)
        await sql`INSERT INTO garage_trailers(user_id,trailer_key,trailer_name,brand,model,license_plate,profile_name,owned_from_save,active,updated_at) VALUES(${user.id},${trailerKey},${name},${brand||null},${model||null},${plate||null},${clean(item?.profileName,160)||null},TRUE,TRUE,NOW()) ON CONFLICT(user_id,trailer_key) DO UPDATE SET trailer_name=EXCLUDED.trailer_name,brand=EXCLUDED.brand,model=EXCLUDED.model,license_plate=EXCLUDED.license_plate,profile_name=EXCLUDED.profile_name,active=TRUE,updated_at=NOW()`
      }
      // Inventário é histórico/persistente: desacoplar ou trocar o reboque não
      // apaga nem desativa o patrimônio já descoberto. A telemetria só confirma
      // presença/uso e adiciona novos reboques quando aparecerem.
      return c.json({ok:true,synced:trailers.length,inventoryPreserved:true})
    }catch(error){console.error('garage_trailers_sync_error',error);return c.json({ok:false,error:'Erro ao sincronizar os reboques.'},500)}
  })
  app.delete('/me/garage/trailers/:id',async(c:any)=>{
    const user=await currentUser(c);if(!user)return unauthorized(c)
    const id=clean(c.req.param('id'),64)
    try{
      const sql=neon(c.env.DATABASE_URL)
      const rows=await sql`UPDATE garage_trailers SET active=FALSE,updated_at=NOW() WHERE id=${id} AND user_id=${user.id} RETURNING id`
      if(!rows[0])return c.json({ok:false,error:'Reboque não encontrado.'},404)
      return c.json({ok:true})
    }catch(error){console.error('garage_trailer_delete_error',error);return c.json({ok:false,error:'Erro ao remover o reboque.'},500)}
  })
}
