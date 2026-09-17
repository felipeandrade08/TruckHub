import { describe, it, expect } from 'vitest'
import { calculateTripEconomy, cargoKey } from './economy'

const settings = {
  fuel_price_brl: 5.98,
  minimum_margin_pct: 20,
  maintenance_brl_km: 0.42,
  efficiency_bonus_pct: 5,
  efficiency_target_l_km: 0.45,
  clean_delivery_bonus_pct: 5,
  damage_tolerance: 0.01,
  damage_penalty_pct: 15,
  weight_surcharge_brl_ton_km: 0.015,
  free_weight_tons: 20,
  minimum_trip_revenue_brl: 150,
}

/** Viagem longa e rentável: a tarifa cobre o custo com folga. */
const profitable = {
  distanceKm: 800,
  fuelLiters: 280,
  massKg: 24000,
  cargoDamage: 0,
  rateBrlKm: 3.2,
  settings,
}

describe('cargoKey', () => {
  it('classifica cargas conhecidas ignorando acentos e caixa', () => {
    expect(cargoKey('Carvão')).toBe('carvao')
    expect(cargoKey('MILHO A GRANEL')).toBe('milho')
    expect(cargoKey('Soja em grãos')).toBe('soja')
    expect(cargoKey('Transformador industrial')).toBe('pesada')
    expect(cargoKey('Automóveis novos')).toBe('veiculos')
  })

  it('cai na tarifa geral quando não reconhece a carga', () => {
    expect(cargoKey('Caixas diversas')).toBe('default')
    expect(cargoKey(null)).toBe('default')
  })
})

describe('🚚 pagamento por km', () => {
  it('multiplica distância pela tarifa da carga', () => {
    const r = calculateTripEconomy(profitable)
    expect(r.kmRevenue).toBe(800 * 3.2)
  })
})

describe('⚖️ adicional por peso', () => {
  it('não cobra adicional dentro do peso livre', () => {
    const r = calculateTripEconomy({ ...profitable, massKg: 18000 })
    expect(r.weightSurcharge).toBe(0)
  })

  it('cobra apenas as toneladas excedentes', () => {
    const r = calculateTripEconomy({ ...profitable, massKg: 24000 })
    // 4 toneladas acima de 20 x 800 km x 0,015
    expect(r.weightSurcharge).toBeCloseTo(4 * 800 * 0.015, 2)
  })

  it('carga mais pesada paga mais que carga leve na mesma rota', () => {
    const leve = calculateTripEconomy({ ...profitable, massKg: 15000 })
    const pesada = calculateTripEconomy({ ...profitable, massKg: 40000 })
    expect(pesada.baseRevenue).toBeGreaterThan(leve.baseRevenue)
  })
})

describe('⛽ custo de combustível pela telemetria', () => {
  it('usa os litros consumidos vezes o preço do diesel', () => {
    const r = calculateTripEconomy(profitable)
    expect(r.fuelCost).toBeCloseTo(280 * 5.98, 2)
  })

  it('não cobra combustível quando a telemetria não registrou consumo', () => {
    const r = calculateTripEconomy({ ...profitable, fuelLiters: 0 })
    expect(r.fuelCost).toBe(0)
  })
})

describe('🛠️ custo de manutenção', () => {
  it('cobra por quilômetro rodado', () => {
    const r = calculateTripEconomy(profitable)
    expect(r.maintenanceCost).toBeCloseTo(800 * 0.42, 2)
  })
})

describe('📊 margem mínima de segurança', () => {
  it('eleva o frete quando a tarifa não cobre o custo operacional', () => {
    // Tarifa irrisória: a receita bruta seria muito menor que o custo.
    const r = calculateTripEconomy({ ...profitable, rateBrlKm: 0.1 })
    expect(r.marginApplied).toBe(true)
    expect(r.protectedRevenue).toBeGreaterThan(r.baseRevenue)
    expect(r.protectedRevenue).toBeCloseTo(r.operatingCost * 1.2, 1)
  })

  it('nunca aceita frete abaixo do custo operacional', () => {
    const r = calculateTripEconomy({ ...profitable, rateBrlKm: 0.1 })
    expect(r.grossRevenue).toBeGreaterThan(r.operatingCost)
    expect(r.netBeforeLoan).toBeGreaterThan(0)
  })

  it('não mexe no frete quando a tarifa já é suficiente', () => {
    const r = calculateTripEconomy(profitable)
    expect(r.marginApplied).toBe(false)
    expect(r.protectedRevenue).toBe(r.baseRevenue)
  })
})

describe('⭐ bônus de eficiência', () => {
  it('paga quando o consumo fica na meta ou abaixo', () => {
    // 800 km com 280 L = 0,35 L/km, abaixo da meta de 0,45
    const r = calculateTripEconomy(profitable)
    expect(r.consumptionLKm).toBeCloseTo(0.35, 2)
    expect(r.efficiencyBonus).toBeGreaterThan(0)
  })

  it('não paga quando o consumo passa da meta', () => {
    const r = calculateTripEconomy({ ...profitable, fuelLiters: 500 })
    expect(r.efficiencyBonus).toBe(0)
  })
})

describe('⭐ bônus de entrega sem avaria', () => {
  it('paga com a carga intacta', () => {
    const r = calculateTripEconomy(profitable)
    expect(r.cleanDelivery).toBe(true)
    expect(r.cleanDeliveryBonus).toBeCloseTo(r.protectedRevenue * 0.05, 2)
    expect(r.damagePenalty).toBe(0)
  })

  it('perde o bônus e aplica penalidade com carga avariada', () => {
    const r = calculateTripEconomy({ ...profitable, cargoDamage: 0.4 })
    expect(r.cleanDelivery).toBe(false)
    expect(r.cleanDeliveryBonus).toBe(0)
    expect(r.damagePenalty).toBeGreaterThan(0)
  })

  it('entrega avariada rende menos que entrega limpa', () => {
    const limpa = calculateTripEconomy(profitable)
    const avariada = calculateTripEconomy({ ...profitable, cargoDamage: 0.5 })
    expect(avariada.grossRevenue).toBeLessThan(limpa.grossRevenue)
  })

  it('respeita a tolerância de avaria mínima', () => {
    const r = calculateTripEconomy({ ...profitable, cargoDamage: 0.005 })
    expect(r.cleanDelivery).toBe(true)
    expect(r.cleanDeliveryBonus).toBeGreaterThan(0)
  })
})

describe('robustez', () => {
  it('não quebra com valores ausentes ou inválidos', () => {
    const r = calculateTripEconomy({
      distanceKm: NaN, fuelLiters: undefined as any, massKg: null as any,
      cargoDamage: NaN, rateBrlKm: undefined as any, settings: {},
    })
    expect(Number.isFinite(r.grossRevenue)).toBe(true)
    expect(r.grossRevenue).toBeGreaterThanOrEqual(0)
  })

  it('nunca devolve receita negativa', () => {
    const r = calculateTripEconomy({ ...profitable, cargoDamage: 1, rateBrlKm: 0 })
    expect(r.grossRevenue).toBeGreaterThanOrEqual(0)
  })

  it('limita a avaria ao intervalo de 0 a 1', () => {
    const acima = calculateTripEconomy({ ...profitable, cargoDamage: 5 })
    const abaixo = calculateTripEconomy({ ...profitable, cargoDamage: -3 })
    expect(acima.cargoDamage).toBe(1)
    expect(abaixo.cargoDamage).toBe(0)
  })
})
