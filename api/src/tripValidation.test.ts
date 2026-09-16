import { describe, expect, it } from 'vitest'
import { isUuid, parseTripMetric, parseTripStart, parseTripText } from './tripValidation'

describe('tripValidation',()=>{
  it('accepts UUID and rejects malformed values',()=>{
    expect(isUuid('550e8400-e29b-41d4-a716-446655440000')).toBe(true)
    expect(isUuid('not-a-uuid')).toBe(false)
    expect(isUuid('')).toBe(false)
  })

  it('limits trip text',()=>{
    expect(parseTripText('  Carga de teste  ',30)).toBe('Carga de teste')
    expect(parseTripText('',30)).toBeNull()
    expect(parseTripText('12345',4)).toBeNull()
    expect(parseTripText(123,30)).toBeNull()
  })

  it('accepts finite metrics inside the configured range only',()=>{
    expect(parseTripMetric('120',0,250)).toBe(120)
    expect(parseTripMetric(250,0,250)).toBe(250)
    expect(parseTripMetric(-1,0,250)).toBeNull()
    expect(parseTripMetric(251,0,250)).toBeNull()
    expect(parseTripMetric('Infinity',0,250)).toBeNull()
  })

  it('rejects invalid or unsafe dates',()=>{
    expect(parseTripStart('not-a-date')).toBeNull()
    expect(parseTripStart(new Date(Date.now()+10*60*1000).toISOString())).toBeNull()
    expect(parseTripStart(new Date(Date.now()-49*60*60*1000).toISOString())).toBeNull()
    expect(parseTripStart(new Date(Date.now()-60*1000).toISOString())).toBeInstanceOf(Date)
  })
})
