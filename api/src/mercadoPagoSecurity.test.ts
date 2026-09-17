import { describe, expect, it } from 'vitest'
import {
  approvedPaymentIsValid,
  constantTimeEqual,
  parseSignature,
  paymentIdentityMatches,
  shouldUnlockLicense,
  webhookTimestampIsFresh,
} from './mercadoPagoSecurity'

describe('Mercado Pago payment identity security', () => {
  const local = {
    id: 'local-payment-1',
    user_id: 'user-1',
    amount: 49.9,
    currency: 'BRL',
    status: 'pending',
    preference_id: 'pref-123',
    external_id: null,
  }

  const remote = {
    id: '987654321',
    external_reference: '11111111-1111-4111-8111-111111111111',
    preference_id: 'pref-123',
    transaction_amount: 49.9,
    currency_id: 'BRL',
    status: 'approved',
  }

  it('accepts a complete payment identity match', () => {
    expect(paymentIdentityMatches(local, remote)).toBe(true)
    expect(approvedPaymentIsValid(local, remote, 49.9)).toBe(true)
    expect(shouldUnlockLicense(local, remote, 49.9)).toBe(true)
  })

  it('rejects a payment already bound to another Mercado Pago payment id', () => {
    expect(paymentIdentityMatches({ ...local, external_id: 'different-payment' }, remote)).toBe(false)
    expect(shouldUnlockLicense({ ...local, external_id: 'different-payment' }, remote, 49.9)).toBe(false)
  })

  it('rejects a preference mismatch', () => {
    const changed = { ...remote, preference_id: 'different-preference' }
    expect(paymentIdentityMatches(local, changed)).toBe(false)
    expect(shouldUnlockLicense(local, changed, 49.9)).toBe(false)
  })

  it('rejects a non-approved payment', () => {
    expect(approvedPaymentIsValid(local, { ...remote, status: 'pending' }, 49.9)).toBe(false)
    expect(shouldUnlockLicense(local, { ...remote, status: 'pending' }, 49.9)).toBe(false)
  })

  it('rejects an amount mismatch', () => {
    expect(approvedPaymentIsValid(local, { ...remote, transaction_amount: 50 }, 49.9)).toBe(false)
    expect(shouldUnlockLicense(local, { ...remote, transaction_amount: 50 }, 49.9)).toBe(false)
  })

  it('rejects a configured price mismatch', () => {
    expect(approvedPaymentIsValid(local, remote, 59.9)).toBe(false)
    expect(shouldUnlockLicense(local, remote, 59.9)).toBe(false)
  })

  it('rejects a currency mismatch or missing currency', () => {
    expect(approvedPaymentIsValid(local, { ...remote, currency_id: 'USD' }, 49.9)).toBe(false)
    expect(approvedPaymentIsValid(local, { ...remote, currency_id: null }, 49.9)).toBe(false)
  })

  it('rejects a missing external reference', () => {
    expect(paymentIdentityMatches(local, { ...remote, external_reference: '   ' })).toBe(false)
  })

  it('parses only valid webhook signatures', () => {
    expect(parseSignature('ts=1750000000,v1=' + 'a'.repeat(64))).toEqual({ ts: '1750000000', v1: 'a'.repeat(64) })
    expect(parseSignature('ts=invalid,v1=' + 'a'.repeat(64))).toBeNull()
    expect(parseSignature('ts=1750000000,v1=bad')).toBeNull()
  })

  it('accepts fresh timestamps and rejects stale timestamps', () => {
    expect(webhookTimestampIsFresh('1750000000', 1750000000)).toBe(true)
    expect(webhookTimestampIsFresh('1750000000', 1750000300)).toBe(true)
    expect(webhookTimestampIsFresh('1750000000', 1750000301)).toBe(false)
    expect(webhookTimestampIsFresh('1750000000000', 1750000000)).toBe(true)
  })

  it('uses constant-time equality semantics for equal-length values', () => {
    expect(constantTimeEqual('abc', 'abc')).toBe(true)
    expect(constantTimeEqual('abc', 'abd')).toBe(false)
    expect(constantTimeEqual('abc', 'ab')).toBe(false)
  })
})
