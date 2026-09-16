import { neon } from '@neondatabase/serverless'

const enc=new TextEncoder()
async function hash(v:string){const d=await crypto.subtle.digest('SHA-256',enc.encode(v));let s='';for(const b of new Uint8Array(d))s+=String.fromCharCode(b);return btoa(s)}
function cookie(req:Request,name:string){const raw=req.headers.get('Cookie')??'';for(const p of raw.split(';')){const [k,...v]=p.trim().split('=');if(k===name){try{return decodeURIComponent(v.join('='))}catch{return v.join('=')}}}return null}
export async function requireAdminMutation(c:any){if(!c.env.DATABASE_URL)return null;const t=cookie(c.req.raw,'truckhub_admin_session'),csrf=cookie(c.req.raw,'truckhub_admin_csrf'),header=c.req.header('X-TruckHub-Admin-CSRF')??'';if(!t||!csrf||!header||header!==csrf)return null;const sql=neon(c.env.DATABASE_URL),h=await hash(t),r=await sql`SELECT a.id,a.email,s.csrf_token_hash FROM admin_sessions s JOIN admin_users a ON a.id=s.admin_user_id WHERE s.token_hash=${h} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND a.status='active' LIMIT 1`;if(!r[0])return null;return (await hash(csrf))===r[0].csrf_token_hash?r[0]:null}
