const UUID_RE=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

export function isUuid(value:unknown):value is string{return typeof value==='string'&&UUID_RE.test(value)}

export function parseTripText(value:unknown,maxLength:number){
  if(value==null)return null
  if(typeof value!=='string')return null
  const v=value.trim()
  return v.length<=maxLength?(v||null):null
}

export function parseTripStart(value:unknown){
  if(value==null||value==='')return new Date()
  if(typeof value!=='string')return null
  const d=new Date(value)
  if(Number.isNaN(d.getTime()))return null
  const now=Date.now()
  if(d.getTime()>now+5*60*1000||d.getTime()<now-48*60*60*1000)return null
  return d
}

export function parseTripMetric(value:unknown,min:number,max:number){
  if(value==null||value==='')return null
  const n=typeof value==='number'?value:Number(value)
  return Number.isFinite(n)&&n>=min&&n<=max?n:null
}
