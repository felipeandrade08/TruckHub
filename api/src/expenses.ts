import { neon } from '@neondatabase/serverless'

interface Env { DATABASE_URL?: string }
const SESSION_COOKIE='truckhub_session'
const UUID_RE=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
const enc=new TextEncoder()
async function hashSessionToken(token:string){const digest=await crypto.subtle.digest('SHA-256',enc.encode(token));let binary='';for(const byte of new Uint8Array(digest))binary+=String.fromCharCode(byte);return btoa(binary)}
function getCookie(request:Request,name:string){const header=request.headers.get('Cookie')??'';for(const part of header.split(';')){const [key,...value]=part.trim().split('=');if(key===name){try{return decodeURIComponent(value.join('='))}catch{return value.join('=')}}}return null}
function jsonError(message:string,status:400|401|404|500){return new Response(JSON.stringify({ok:false,error:message}),{status,headers:{'content-type':'application/json; charset=UTF-8','cache-control':'no-store'}})}
async function requireUser(c:any){if(!c.env.DATABASE_URL)return null;const token=getCookie(c.req.raw,SESSION_COOKIE)||c.req.header('Authorization')?.replace(/^Bearer\s+/i,'').trim();if(!token)return null;const tokenHash=await hashSessionToken(token);const sql=neon(c.env.DATABASE_URL);const rows=await sql`SELECT u.id,u.name,u.email FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${tokenHash} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND s.session_type IN ('web','desktop') AND u.status='active' LIMIT 1`;return rows[0]??null}
async function readJson(c:any){try{return await c.req.json()}catch{return null}}
const EXPENSE_TYPES=new Set(['fuel','toll','maintenance','parking','food','other'])
export function registerExpenseRoutes(app:any){
 app.get('/me/expenses',async c=>{try{const user=await requireUser(c);if(!user)return jsonError('Sessão inválida ou expirada.',401);const sql=neon(c.env.DATABASE_URL!);const rows=await sql`SELECT e.id,e.trip_id,e.type,e.description,e.amount,e.created_at,t.cargo,t.origin,t.destination,tr.truck_name FROM expenses e LEFT JOIN trips t ON t.id=e.trip_id LEFT JOIN trucks tr ON tr.id=t.truck_id AND tr.user_id=e.user_id WHERE e.user_id=${user.id} ORDER BY e.created_at DESC LIMIT 500`;return c.json({ok:true,expenses:rows},{headers:{'Cache-Control':'no-store'}})}catch(error){console.error('expenses_list_error',error);return jsonError('Erro ao carregar despesas.',500)}})
 app.post('/me/expenses',async c=>{try{const user=await requireUser(c);if(!user)return jsonError('Sessão inválida ou expirada.',401);const data=await readJson(c);if(!data)return jsonError('JSON inválido.',400);const type=String(data.type??'').trim().toLowerCase(),description=String(data.description??'').trim()||null,amount=Number(data.amount),tripId=data.tripId?String(data.tripId).trim():null;if(!EXPENSE_TYPES.has(type))return jsonError('Tipo de despesa inválido.',400);if(!Number.isFinite(amount)||amount<0||amount>10000000)return jsonError('Valor da despesa inválido.',400);if(description&&description.length>255)return jsonError('Descrição muito longa.',400);if(tripId&&!UUID_RE.test(tripId))return jsonError('Identificador da viagem inválido.',400);const sql=neon(c.env.DATABASE_URL!);if(tripId){const trip=await sql`SELECT id FROM trips WHERE id=${tripId} AND user_id=${user.id} LIMIT 1`;if(!trip[0])return jsonError('Viagem inválida.',400)}const rows=await sql`INSERT INTO expenses(user_id,trip_id,type,description,amount) VALUES(${user.id},${tripId},${type},${description},${amount}) RETURNING id,trip_id,type,description,amount,created_at`;return c.json({ok:true,expense:rows[0]},{status:201})}catch(error){console.error('expense_create_error',error);return jsonError('Erro ao registrar despesa.',500)}})
 app.post('/me/expenses/fuel-payment',async c=>{try{
   const user=await requireUser(c);if(!user)return jsonError('Sessão inválida ou expirada.',401)
   const data=await readJson(c);if(!data)return jsonError('JSON inválido.',400)
   const liters=Number(data.liters),price=Number(data.pricePerLiter),amount=Number(data.amount)
   const station=String(data.station??'').trim(),city=String(data.city??'').trim(),tripId=data.tripId?String(data.tripId).trim():null
   if(!Number.isFinite(liters)||liters<=0||liters>2000)return jsonError('Quantidade de litros inválida.',400)
   if(!Number.isFinite(price)||price<=0||price>1000)return jsonError('Preço por litro inválido.',400)
   const expected=Number((liters*price).toFixed(2));if(!Number.isFinite(amount)||amount<=0||Math.abs(amount-expected)>0.01)return jsonError('Valor do abastecimento não confere com litros x preço.',400)
   if(!station||station.length>160)return jsonError('Posto inválido.',400);if(city.length>120)return jsonError('Cidade inválida.',400)
   if(tripId&&!UUID_RE.test(tripId))return jsonError('Identificador da viagem inválido.',400)
   const sql=neon(c.env.DATABASE_URL!);if(tripId){const trip=await sql`SELECT id FROM trips WHERE id=${tripId} AND user_id=${user.id} LIMIT 1`;if(!trip[0])return jsonError('Viagem inválida.',400)}
   await sql`INSERT INTO economy_accounts(user_id,balance_brl) VALUES(${user.id},0) ON CONFLICT(user_id) DO NOTHING`
   const account=await sql`SELECT balance_brl FROM economy_accounts WHERE user_id=${user.id}`
   const before=Number(account[0]?.balance_brl||0),after=Number((before-expected).toFixed(2))
   const description=`Abastecimento • ${liters.toFixed(1)} L x R$ ${price.toFixed(2)}/L • ${station}${city?` • ${city}`:''}`
   const expense=await sql`INSERT INTO expenses(user_id,trip_id,type,description,amount) VALUES(${user.id},${tripId},'fuel',${description},${expected}) RETURNING id,trip_id,type,description,amount,created_at`
   await sql`UPDATE economy_accounts SET balance_brl=${after},updated_at=NOW() WHERE user_id=${user.id}`
   await sql`INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata) VALUES(${user.id},${tripId},'fuel_payment',${description},${-expected},${after},${JSON.stringify({liters,pricePerLiter:price,station,city,odometerKm:data.odometerKm,truckBrand:data.truckBrand,truckModel:data.truckModel,licensePlate:data.licensePlate})})`
   return c.json({ok:true,expense:expense[0],debitedBrl:expected,balanceBrl:after},{status:201})
 }catch(error){console.error('fuel_payment_error',error);return jsonError('Erro ao processar o pagamento do abastecimento.',500)}})
 app.delete('/me/expenses/:id',async c=>{try{const user=await requireUser(c);if(!user)return jsonError('Sessão inválida ou expirada.',401);const id=c.req.param('id');if(!UUID_RE.test(id))return jsonError('Identificador da despesa inválido.',400);const sql=neon(c.env.DATABASE_URL!);const rows=await sql`DELETE FROM expenses WHERE id=${id} AND user_id=${user.id} RETURNING id`;if(!rows[0])return jsonError('Despesa não encontrada.',404);return c.json({ok:true})}catch(error){console.error('expense_delete_error',error);return jsonError('Erro ao remover despesa.',500)}})
}
