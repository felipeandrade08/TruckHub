import { neon } from '@neondatabase/serverless'

const UUID_RE=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
function getCookie(request:Request,name:string){const header=request.headers.get('Cookie')??'';for(const part of header.split(';')){const [key,...value]=part.trim().split('=');if(key===name)return value.join('=')}return null}
function toBase64(bytes:Uint8Array){let binary='';for(const byte of bytes)binary+=String.fromCharCode(byte);return btoa(binary)}
async function hashSessionToken(token:string){const digest=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(token));return toBase64(new Uint8Array(digest))}
function normalizeCargo(value:string|null){return (value??'').normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase()}
function cargoKey(cargo:string|null){const s=normalizeCargo(cargo);if(/carvao|carvao vegetal/.test(s))return 'carvao';if(/milho/.test(s))return 'milho';if(/soja/.test(s))return 'soja';if(/carro|veiculo|veiculos|automovel/.test(s))return 'veiculos';if(/pesad|maquina|equipamento|transformador/.test(s))return 'pesada';if(/especial/.test(s))return 'especial';return 'default'}
function weightMultiplier(kg:number){const t=kg/1000;if(t<=20)return 1;if(t<=30)return 1.08;if(t<=40)return 1.18;return 1.30}
function round2(n:number){return Number(n.toFixed(2))}

export async function settleTripEconomy(sql:any,userId:string,tripId:string){
  const rows=await sql`SELECT t.id,t.user_id,t.cargo,t.cargo_mass_kg,t.distance_km,t.fuel_used_l,t.status,t.cargo_value_brl,COALESCE(th.max_speed_kph,0) AS max_speed_kph,COALESCE(th.average_speed_kph,0) AS average_speed_kph FROM trips t LEFT JOIN tachographs th ON th.trip_id=t.id WHERE t.id=${tripId} AND t.user_id=${userId} LIMIT 1`
  const trip=rows[0];if(!trip)return null
  const existing=await sql`SELECT id FROM economy_ledger WHERE trip_id=${tripId} AND entry_type='trip_income' LIMIT 1`;if(existing[0])return {alreadySettled:true}
  const settingsRows=await sql`SELECT fuel_price_brl,minimum_margin_pct,maintenance_pct_of_revenue,efficiency_bonus_pct,clean_delivery_bonus_pct FROM economy_settings WHERE id=TRUE LIMIT 1`
  const settings=settingsRows[0]??{fuel_price_brl:5.98,minimum_margin_pct:20,maintenance_pct_of_revenue:6,efficiency_bonus_pct:5,clean_delivery_bonus_pct:5}
  const rateRows=await sql`SELECT rate_brl_km FROM cargo_rates WHERE cargo_key=${cargoKey(trip.cargo)} AND active=TRUE LIMIT 1`
  const fallback=await sql`SELECT rate_brl_km FROM cargo_rates WHERE cargo_key='default' LIMIT 1`
  const rate=Number(rateRows[0]?.rate_brl_km??fallback[0]?.rate_brl_km??2.8)
  const distance=Math.max(0,Number(trip.distance_km)||0)
  const fuelLiters=Math.max(0,Number(trip.fuel_used_l)||0)
  const massKg=Math.max(0,Number(trip.cargo_mass_kg)||0)
  const baseRevenue=distance*rate*weightMultiplier(massKg)
  const fuelCost=fuelLiters*Number(settings.fuel_price_brl)
  const maintenance=baseRevenue*(Number(settings.maintenance_pct_of_revenue)/100)
  const marginFloor=(fuelCost+maintenance)*(1+Number(settings.minimum_margin_pct)/100)
  const protectedRevenue=Math.max(baseRevenue,marginFloor)
  const fuelEfficiencyBonus=fuelCost>0&&distance>0&&fuelLiters/distance<=0.45?protectedRevenue*(Number(settings.efficiency_bonus_pct)/100):0
  const cleanDeliveryBonus=protectedRevenue*(Number(settings.clean_delivery_bonus_pct)/100)
  const gross=round2(protectedRevenue+fuelEfficiencyBonus+cleanDeliveryBonus)
  const tripExpenses=await sql`SELECT COALESCE(SUM(amount),0) AS total FROM expenses WHERE trip_id=${tripId} AND user_id=${userId}`
  const alreadyRecordedFuel=Number(tripExpenses[0]?.total||0)
  const hasFuelExpense=await sql`SELECT id FROM expenses WHERE trip_id=${tripId} AND user_id=${userId} AND type='fuel' LIMIT 1`
  if(fuelCost>0&&!hasFuelExpense[0])await sql`INSERT INTO expenses(user_id,trip_id,type,description,amount) VALUES(${userId},${tripId},'fuel','Abastecimento calculado automaticamente pela telemetria',${round2(fuelCost)})`
  const netBeforeBank=round2(gross-maintenance-(hasFuelExpense[0]?0:fuelCost)-alreadyRecordedFuel)
  const account=await sql`INSERT INTO economy_accounts(user_id,balance_brl) VALUES(${userId},0) ON CONFLICT(user_id) DO UPDATE SET updated_at=economy_accounts.updated_at RETURNING balance_brl`
  let balance=Number(account[0]?.balance_brl||0)
  const loanRows=await sql`SELECT id,remaining_brl,repayment_pct FROM economy_loans WHERE user_id=${userId} AND status='active' ORDER BY created_at ASC LIMIT 1`
  const loan=loanRows[0]
  const loanPayment=loan?Math.min(Number(loan.remaining_brl),Math.max(0,gross)*(Number(loan.repayment_pct)/100)):0
  const bankCredit=round2(Math.max(0,netBeforeBank-loanPayment))
  balance=round2(balance+bankCredit)
  await sql`UPDATE economy_accounts SET balance_brl=${balance},updated_at=NOW() WHERE user_id=${userId}`
  await sql`INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata) VALUES(${userId},${tripId},'trip_income','Fechamento automático da viagem',${bankCredit},${balance},${JSON.stringify({cargo:trip.cargo,distanceKm:distance,rateBrlKm:rate,weightKg:massKg,weightMultiplier:weightMultiplier(massKg),fuelLiters,fuelPriceBrl:Number(settings.fuel_price_brl),fuelCost,maintenance,baseRevenue:round2(baseRevenue),protectedRevenue:round2(protectedRevenue),efficiencyBonus:round2(fuelEfficiencyBonus),cleanDeliveryBonus:round2(cleanDeliveryBonus),gross,loanPayment,netBeforeBank})}`
  if(loan&&loanPayment>0){const remaining=round2(Number(loan.remaining_brl)-loanPayment);await sql`UPDATE economy_loans SET remaining_brl=${remaining},status=${remaining<=0?'paid':'active'},paid_at=${remaining<=0?new Date().toISOString():null} WHERE id=${loan.id}`;await sql`INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata) VALUES(${userId},${tripId},'loan_payment','Parcela automática do empréstimo',${-round2(loanPayment)},${round2(balance+loanPayment)},${JSON.stringify({loanId:loan.id})})`}
  return {alreadySettled:false,distanceKm:round2(distance),fuelLiters:round2(fuelLiters),fuelPriceBrl:Number(settings.fuel_price_brl),fuelCost:round2(fuelCost),rateBrlKm:rate,weightMultiplier:weightMultiplier(massKg),baseRevenue:round2(baseRevenue),protectedRevenue:round2(protectedRevenue),maintenance:round2(maintenance),efficiencyBonus:round2(fuelEfficiencyBonus),cleanDeliveryBonus:round2(cleanDeliveryBonus),gross,loanPayment:round2(loanPayment),netBrl:bankCredit,balanceBrl:balance}
}

export function registerEconomyRoutes(app:any){
  app.get('/me/economy',async(c:any)=>{if(!c.env.DATABASE_URL)return c.json({ok:false,error:'Banco de dados não configurado.'},{status:500});const token=getCookie(c.req.raw,'truckhub_session');if(!token)return c.json({ok:false,error:'Sessão inválida ou expirada.'},{status:401});try{const tokenHash=await hashSessionToken(token),sql=neon(c.env.DATABASE_URL),users=await sql`SELECT u.id,u.name FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${tokenHash} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND u.status='active' LIMIT 1`,user=users[0];if(!user)return c.json({ok:false,error:'Sessão inválida ou expirada.'},{status:401});const account=await sql`SELECT balance_brl FROM economy_accounts WHERE user_id=${user.id} LIMIT 1`;const ledger=await sql`SELECT id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata,created_at FROM economy_ledger WHERE user_id=${user.id} ORDER BY created_at DESC LIMIT 50`;const loan=await sql`SELECT id,principal_brl,remaining_brl,repayment_pct,status,created_at,paid_at FROM economy_loans WHERE user_id=${user.id} AND status='active' ORDER BY created_at DESC LIMIT 1`;return c.json({ok:true,account:{balanceBrl:Number(account[0]?.balance_brl||0)},loan:loan[0]??null,ledger},{headers:{'Cache-Control':'no-store'}})}catch(e){console.error('economy_get_error',e);return c.json({ok:false,error:'Erro ao carregar a conta financeira.'},{status:500})}})
  app.get('/me/trips/:id/economy-preview',async(c:any)=>{if(!c.env.DATABASE_URL)return c.json({ok:false,error:'Banco de dados não configurado.'},{status:500});const id=c.req.param('id');if(!UUID_RE.test(id))return c.json({ok:false,error:'Viagem não encontrada.'},{status:404});const token=getCookie(c.req.raw,'truckhub_session');if(!token)return c.json({ok:false,error:'Sessão inválida ou expirada.'},{status:401});try{const tokenHash=await hashSessionToken(token),sql=neon(c.env.DATABASE_URL),users=await sql`SELECT u.id FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=${tokenHash} AND s.revoked_at IS NULL AND s.expires_at>NOW() AND u.status='active' LIMIT 1`,user=users[0];if(!user)return c.json({ok:false,error:'Sessão inválida ou expirada.'},{status:401});const rows=await sql`SELECT id,cargo,cargo_mass_kg,distance_km,fuel_used_l,status FROM trips WHERE id=${id} AND user_id=${user.id} LIMIT 1`;const t=rows[0];if(!t)return c.json({ok:false,error:'Viagem não encontrada.'},{status:404});const s=(await sql`SELECT fuel_price_brl,minimum_margin_pct,maintenance_pct_of_revenue FROM economy_settings WHERE id=TRUE LIMIT 1`)[0]??{fuel_price_brl:5.98,minimum_margin_pct:20,maintenance_pct_of_revenue:6};const r=(await sql`SELECT rate_brl_km FROM cargo_rates WHERE cargo_key=${cargoKey(t.cargo)} AND active=TRUE LIMIT 1`)[0];const rate=Number(r?.rate_brl_km??2.8),distance=Math.max(0,Number(t.distance_km)||0),fuel=Math.max(0,Number(t.fuel_used_l)||0),mass=Math.max(0,Number(t.cargo_mass_kg)||0),base=distance*rate*weightMultiplier(mass),fuelCost=fuel*Number(s.fuel_price_brl),maintenance=base*Number(s.maintenance_pct_of_revenue)/100,minRevenue=(fuelCost+maintenance)*(1+Number(s.minimum_margin_pct)/100),revenue=Math.max(base,minRevenue);return c.json({ok:true,preview:{cargo:t.cargo,distanceKm:round2(distance),fuelLiters:round2(fuel),fuelPriceBrl:Number(s.fuel_price_brl),fuelCost:round2(fuelCost),rateBrlKm:rate,weightMultiplier:weightMultiplier(mass),baseRevenue:round2(base),minimumSustainableRevenue:round2(minRevenue),projectedRevenue:round2(revenue),maintenance:round2(maintenance),status:t.status}},{headers:{'Cache-Control':'no-store'}})}catch(e){console.error('economy_preview_error',e);return c.json({ok:false,error:'Erro ao calcular a prévia econômica.'},{status:500})}})
}
