export const WEBHOOK_MAX_AGE_SECONDS = 5 * 60

export interface LocalPaymentIdentity {
  id: string
  user_id: string
  amount: number | string
  currency: string
  status: string
  preference_id: string | null
  external_id: string | null
}

export interface MercadoPagoPaymentIdentity {
  id: string | number
  external_reference: string
  preference_id?: string | number | null
  transaction_amount: number
  currency_id?: string | null
  status: string
}

export function parseSignature(header: string | null) {
  if (!header) return null
  const values: Record<string, string> = {}
  for (const item of header.split(',')) {
    const [key, ...rest] = item.trim().split('=')
    if (key && rest.length) values[key] = rest.join('=').trim()
  }
  if (!/^\d{1,16}$/.test(values.ts || '')) return null
  if (!/^[a-fA-F0-9]{64}$/.test(values.v1 || '')) return null
  return { ts: values.ts, v1: values.v1 }
}

export function webhookTimestampIsFresh(rawTimestamp: string, nowSeconds = Math.floor(Date.now() / 1000)) {
  const numeric = Number(rawTimestamp)
  if (!Number.isSafeInteger(numeric)) return false
  const timestamp = numeric > 1e12 ? Math.floor(numeric / 1000) : numeric
  return Math.abs(nowSeconds - timestamp) <= WEBHOOK_MAX_AGE_SECONDS
}

export function constantTimeEqual(a: string, b: string) {
  if (a.length !== b.length) return false
  let result = 0
  for (let i = 0; i < a.length; i++) result |= a.charCodeAt(i) ^ b.charCodeAt(i)
  return result === 0
}

export function paymentIdentityMatches(local: LocalPaymentIdentity, remote: MercadoPagoPaymentIdentity, expectedProvider = 'mercado_pago') {
  const remoteId = String(remote.id).trim()
  const remoteReference = remote.external_reference.trim()
  const remotePreference = remote.preference_id == null ? '' : String(remote.preference_id).trim()
  if (!remoteId || !remoteReference) return false
  if (local.external_id && local.external_id !== remoteId) return false
  if (local.preference_id && (!remotePreference || local.preference_id !== remotePreference)) return false
  if (expectedProvider !== 'mercado_pago') return false
  return true
}

export function approvedPaymentIsValid(
  local: Pick<LocalPaymentIdentity, 'amount' | 'currency'>,
  remote: Pick<MercadoPagoPaymentIdentity, 'transaction_amount' | 'currency_id' | 'status'>,
  configuredPrice: number | null,
) {
  if (remote.status !== 'approved') return false
  if (configuredPrice == null) return false
  if (!remote.currency_id || String(remote.currency_id) !== String(local.currency)) return false
  return Number(remote.transaction_amount) === Number(local.amount)
    && Number(local.amount) === configuredPrice
}

export function shouldUnlockLicense(
  local: LocalPaymentIdentity,
  remote: MercadoPagoPaymentIdentity,
  configuredPrice: number | null,
) {
  return paymentIdentityMatches(local, remote) && approvedPaymentIsValid(local, remote, configuredPrice)
}
