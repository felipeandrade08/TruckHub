const UUID_RE=/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
export function isUuid(value:unknown):value is string{return typeof value==='string'&&UUID_RE.test(value)}
export function parseUuid(value:unknown){return isUuid(value)?value:null}
export function parseFiniteNumber(value:unknown,min:number,max:number){const n=typeof value==='number'?value:Number(value);return Number.isFinite(n)&&n>=min&&n<=max?n:null}
export function parseNonEmptyString(value:unknown,maxLength:number){if(typeof value!=='string')return null;const v=value.trim();return v.length>0&&v.length<=maxLength?v:null}
export function isSafeDate(value:unknown,maxFutureMs=5*60*1000,maxPastMs=48*60*60*1000){const d=value instanceof Date?value:new Date(String(value));if(Number.isNaN(d.getTime()))return null;const now=Date.now();return d.getTime()<=now+maxFutureMs&&d.getTime()>=now-maxPastMs?d:null}
