export interface TruckHubEnv {
  DATABASE_URL?: string
  RATE_LIMIT_PEPPER?: string
  MERCADOPAGO_ACCESS_TOKEN?: string
  MERCADOPAGO_WEBHOOK_SECRET?: string
  TRUCKHUB_PUBLIC_URL?: string
  TRUCKHUB_API_PUBLIC_URL?: string
  TRUCKHUB_LIFETIME_PRICE_BRL?: string
  WEB_ORIGIN?: string
  ENVIRONMENT?: string
}

function required(name: string, value: unknown): string {
  const normalized = String(value ?? '').trim()
  if (!normalized) throw new Error(`${name} não configurada.`)
  return normalized
}

function httpsUrl(name: string, value: unknown): string {
  const normalized = required(name, value).replace(/\/$/, '')
  let url: URL
  try { url = new URL(normalized) } catch { throw new Error(`${name} inválida.`) }
  if (url.protocol !== 'https:') throw new Error(`${name} deve usar HTTPS.`)
  return normalized
}

export function getDatabaseUrl(env: TruckHubEnv): string {
  return required('DATABASE_URL', env.DATABASE_URL)
}

export function getWebOrigin(env: TruckHubEnv): string {
  const origin = required('WEB_ORIGIN', env.WEB_ORIGIN).replace(/\/$/, '')
  let url: URL
  try { url = new URL(origin) } catch { throw new Error('WEB_ORIGIN inválida.') }
  if (url.protocol !== 'https:' && !isDevelopment(env)) throw new Error('WEB_ORIGIN deve usar HTTPS em produção.')
  if (url.username || url.password || url.pathname !== '/' || url.search || url.hash) throw new Error('WEB_ORIGIN deve ser apenas a origem.')
  return origin
}

export function getMercadoPagoConfig(env: TruckHubEnv) {
  return {
    accessToken: required('MERCADOPAGO_ACCESS_TOKEN', env.MERCADOPAGO_ACCESS_TOKEN),
    webhookSecret: required('MERCADOPAGO_WEBHOOK_SECRET', env.MERCADOPAGO_WEBHOOK_SECRET),
    publicUrl: httpsUrl('TRUCKHUB_PUBLIC_URL', env.TRUCKHUB_PUBLIC_URL),
    apiPublicUrl: httpsUrl('TRUCKHUB_API_PUBLIC_URL', env.TRUCKHUB_API_PUBLIC_URL),
  }
}

export function getLifetimePrice(env: TruckHubEnv): number {
  const value = Number(env.TRUCKHUB_LIFETIME_PRICE_BRL)
  if (!Number.isFinite(value) || value <= 0 || value > 100000) {
    throw new Error('TRUCKHUB_LIFETIME_PRICE_BRL inválido.')
  }
  return Math.round(value * 100) / 100
}

export function isProduction(env: TruckHubEnv): boolean {
  return String(env.ENVIRONMENT ?? '').toLowerCase() === 'production'
}

export function isDevelopment(env: TruckHubEnv): boolean {
  return !isProduction(env)
}

export function validateRuntimeConfig(env: TruckHubEnv): { ok: true } {
  getDatabaseUrl(env)
  getWebOrigin(env)
  getMercadoPagoConfig(env)
  getLifetimePrice(env)
  const pepper = required('RATE_LIMIT_PEPPER', env.RATE_LIMIT_PEPPER)
  if (pepper.length < 32) throw new Error('RATE_LIMIT_PEPPER deve possuir pelo menos 32 caracteres.')
  return { ok: true }
}
