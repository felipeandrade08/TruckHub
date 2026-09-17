import { neon } from '@neondatabase/serverless'
import { hashSessionToken, getCookie } from './sharedAuth'

/* ============================================================
   TransPoli — BANCO DO MOTORISTA
   ------------------------------------------------------------
   Regras de remuneração da viagem, em ordem de aplicação:

     1. Receita por km       = distância x tarifa da carga
     2. Adicional por peso   = toneladas excedentes x km x adicional
     3. Margem de segurança  = receita nunca abaixo do custo + margem
     4. Bônus de eficiência  = consumo abaixo da meta L/km
     5. Bônus sem avaria     = carga entregue dentro da tolerância
     6. Penalidade de avaria = desconto quando a carga chega danificada
     7. Custos automáticos   = combustível (litros da telemetria) + manutenção
     8. Parcela do empréstimo= descontada da receita líquida
     9. Saldo + livro-caixa  = tudo registrado em economy_ledger
   ============================================================ */

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

const LOAN_OPTIONS = [5000, 10000]

const DEFAULT_SETTINGS = {
  fuel_price_brl: 5.98,
  minimum_margin_pct: 20,
  maintenance_pct_of_revenue: 6,
  efficiency_bonus_pct: 5,
  clean_delivery_bonus_pct: 5,
  weight_surcharge_brl_ton_km: 0.015,
  free_weight_tons: 20,
  maintenance_brl_km: 0.42,
  efficiency_target_l_km: 0.45,
  damage_tolerance: 0.01,
  damage_penalty_pct: 15,
  minimum_trip_revenue_brl: 150,
}

function round2(n: number) {
  return Number((Number.isFinite(n) ? n : 0).toFixed(2))
}

function num(value: any, fallback = 0) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

function normalizeCargo(value: string | null) {
  return (value ?? '').normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase()
}

/** Classifica a carga do ETS2 em uma das tarifas cadastradas. */
export function cargoKey(cargo: string | null) {
  const s = normalizeCargo(cargo)
  if (/carvao/.test(s)) return 'carvao'
  if (/milho/.test(s)) return 'milho'
  if (/soja/.test(s)) return 'soja'
  if (/combustivel|gasolina|diesel|petroleo|quimic|acido|inflamavel/.test(s)) return 'perigosa'
  if (/refriger|congelad|frigorific|alimento|leite|carne/.test(s)) return 'refrigerada'
  if (/carro|veiculo|automove|trator|caminhonete/.test(s)) return 'veiculos'
  if (/pesad|maquina|equipamento|transformador|turbina|concreto|aco|ferro/.test(s)) return 'pesada'
  if (/especial|superdimension|excedente|gigante/.test(s)) return 'especial'
  return 'default'
}

async function currentUser(c: any) {
  if (!c.env.DATABASE_URL) return null
  const bearer = c.req.header('Authorization')?.replace(/^Bearer\s+/i, '').trim()
  const token = bearer || getCookie(c.req.raw, 'truckhub_session')
  if (!token) return null
  const tokenHash = await hashSessionToken(token)
  const sql = neon(c.env.DATABASE_URL)
  const rows = await sql`
    SELECT u.id, u.name, u.email
      FROM sessions s
      JOIN users u ON u.id = s.user_id
     WHERE s.token_hash = ${tokenHash}
       AND s.revoked_at IS NULL
       AND s.expires_at > NOW()
       AND u.status = 'active'
     LIMIT 1`
  return rows[0] ?? null
}

async function loadSettings(sql: any) {
  try {
    const rows = await sql`SELECT * FROM economy_settings WHERE id = TRUE LIMIT 1`
    return { ...DEFAULT_SETTINGS, ...(rows[0] ?? {}) }
  } catch {
    return { ...DEFAULT_SETTINGS }
  }
}

async function loadRate(sql: any, key: string) {
  const rows = await sql`SELECT rate_brl_km FROM cargo_rates WHERE cargo_key = ${key} AND active = TRUE LIMIT 1`
  if (rows[0]) return num(rows[0].rate_brl_km, 2.8)
  const fallback = await sql`SELECT rate_brl_km FROM cargo_rates WHERE cargo_key = 'default' AND active = TRUE LIMIT 1`
  return num(fallback[0]?.rate_brl_km, 2.8)
}

/* ------------------------------------------------------------
   NÚCLEO DE CÁLCULO — puro, sem banco, para poder ser testado.
------------------------------------------------------------ */
export function calculateTripEconomy(input: {
  distanceKm: number
  fuelLiters: number
  massKg: number
  cargoDamage: number
  rateBrlKm: number
  settings: any
}) {
  const s = input.settings
  const distance = Math.max(0, num(input.distanceKm))
  const fuelLiters = Math.max(0, num(input.fuelLiters))
  const massKg = Math.max(0, num(input.massKg))
  const damage = Math.min(1, Math.max(0, num(input.cargoDamage)))
  const rate = Math.max(0, num(input.rateBrlKm, 2.8))

  // 1. Receita por km
  const kmRevenue = distance * rate

  // 2. Adicional por peso — só o excedente é cobrado
  const tons = massKg / 1000
  const billableTons = Math.max(0, tons - num(s.free_weight_tons, 20))
  const weightSurcharge = billableTons * distance * num(s.weight_surcharge_brl_ton_km, 0.015)

  const baseRevenue = kmRevenue + weightSurcharge

  // 7. Custos automáticos
  const fuelCost = fuelLiters * num(s.fuel_price_brl, 5.98)
  const maintenanceCost = distance * num(s.maintenance_brl_km, 0.42)
  const operatingCost = fuelCost + maintenanceCost

  // 3. Margem mínima de segurança — o frete nunca paga menos que o custo + margem
  const marginFloor = operatingCost * (1 + num(s.minimum_margin_pct, 20) / 100)
  const absoluteFloor = distance > 0 ? num(s.minimum_trip_revenue_brl, 150) : 0
  const revenueFloor = Math.max(marginFloor, absoluteFloor)
  const marginApplied = baseRevenue < revenueFloor
  const protectedRevenue = Math.max(baseRevenue, revenueFloor)

  // 4. Bônus de eficiência
  const consumption = distance > 0 ? fuelLiters / distance : 0
  const efficient = distance >= 10 && fuelLiters > 0 && consumption <= num(s.efficiency_target_l_km, 0.45)
  const efficiencyBonus = efficient ? protectedRevenue * num(s.efficiency_bonus_pct, 5) / 100 : 0

  // 5. Bônus de entrega sem avaria
  const clean = damage <= num(s.damage_tolerance, 0.01)
  const cleanDeliveryBonus = clean ? protectedRevenue * num(s.clean_delivery_bonus_pct, 5) / 100 : 0

  // 6. Penalidade proporcional à avaria
  const damagePenalty = clean ? 0 : protectedRevenue * (num(s.damage_penalty_pct, 15) / 100) * damage

  const grossRevenue = Math.max(0, protectedRevenue + efficiencyBonus + cleanDeliveryBonus - damagePenalty)
  const netBeforeLoan = grossRevenue - operatingCost

  return {
    distanceKm: round2(distance),
    fuelLiters: round2(fuelLiters),
    massKg: round2(massKg),
    cargoDamage: Number(damage.toFixed(4)),
    rateBrlKm: round2(rate),
    kmRevenue: round2(kmRevenue),
    billableTons: round2(billableTons),
    weightSurcharge: round2(weightSurcharge),
    baseRevenue: round2(baseRevenue),
    fuelPriceBrl: round2(num(s.fuel_price_brl, 5.98)),
    fuelCost: round2(fuelCost),
    maintenanceCost: round2(maintenanceCost),
    operatingCost: round2(operatingCost),
    minimumMarginPct: round2(num(s.minimum_margin_pct, 20)),
    revenueFloor: round2(revenueFloor),
    marginApplied,
    protectedRevenue: round2(protectedRevenue),
    consumptionLKm: Number(consumption.toFixed(3)),
    efficiencyTargetLKm: round2(num(s.efficiency_target_l_km, 0.45)),
    efficiencyBonus: round2(efficiencyBonus),
    cleanDelivery: clean,
    cleanDeliveryBonus: round2(cleanDeliveryBonus),
    damagePenalty: round2(damagePenalty),
    grossRevenue: round2(grossRevenue),
    netBeforeLoan: round2(netBeforeLoan),
  }
}

/* ------------------------------------------------------------
   LIQUIDAÇÃO DA VIAGEM — grava no livro-caixa e no saldo.
------------------------------------------------------------ */
export async function settleTripEconomy(sql: any, userId: string, tripId: string) {
  const tripRows = await sql`
    SELECT id, cargo, cargo_mass_kg, distance_km, fuel_used_l, cargo_damage, status
      FROM trips WHERE id = ${tripId} AND user_id = ${userId} LIMIT 1`
  const trip = tripRows[0]
  if (!trip) return null

  const already = await sql`
    SELECT id FROM economy_ledger
     WHERE trip_id = ${tripId} AND entry_type = 'trip_income' LIMIT 1`
  if (already[0]) return { alreadySettled: true }

  const settings = await loadSettings(sql)
  const key = cargoKey(trip.cargo)
  const rate = await loadRate(sql, key)

  const calc = calculateTripEconomy({
    distanceKm: num(trip.distance_km),
    fuelLiters: num(trip.fuel_used_l),
    massKg: num(trip.cargo_mass_kg),
    cargoDamage: num(trip.cargo_damage),
    rateBrlKm: rate,
    settings,
  })

  // Despesas: usa o que o motorista já lançou; completa com o cálculo automático.
  const fuelRow = await sql`
    SELECT COALESCE(SUM(amount),0) total FROM expenses
     WHERE user_id = ${userId} AND trip_id = ${tripId} AND type = 'fuel'`
  let fuelExpense = num(fuelRow[0]?.total)
  if (fuelExpense <= 0 && calc.fuelCost > 0) {
    fuelExpense = calc.fuelCost
    await sql`
      INSERT INTO expenses(user_id, trip_id, type, description, amount)
      VALUES(${userId}, ${tripId}, 'fuel',
             ${`Combustivel: ${calc.fuelLiters} L da telemetria x R$ ${calc.fuelPriceBrl}/L`},
             ${fuelExpense})`
  }

  const maintenanceRow = await sql`
    SELECT COALESCE(SUM(amount),0) total FROM expenses
     WHERE user_id = ${userId} AND trip_id = ${tripId} AND type = 'maintenance'`
  let maintenanceExpense = num(maintenanceRow[0]?.total)
  if (maintenanceExpense <= 0 && calc.maintenanceCost > 0) {
    maintenanceExpense = calc.maintenanceCost
    await sql`
      INSERT INTO expenses(user_id, trip_id, type, description, amount)
      VALUES(${userId}, ${tripId}, 'maintenance',
             ${`Manutencao automatica: ${calc.distanceKm} km x R$ ${num(settings.maintenance_brl_km, 0.42)}/km`},
             ${maintenanceExpense})`
  }

  const allRow = await sql`
    SELECT COALESCE(SUM(amount),0) total FROM expenses
     WHERE user_id = ${userId} AND trip_id = ${tripId}`
  const totalExpenses = round2(num(allRow[0]?.total))

  await sql`INSERT INTO economy_accounts(user_id, balance_brl) VALUES(${userId}, 0) ON CONFLICT(user_id) DO NOTHING`
  const accountRow = await sql`SELECT balance_brl FROM economy_accounts WHERE user_id = ${userId}`
  const openingBalance = round2(num(accountRow[0]?.balance_brl))
  let balance = openingBalance

  // Receita bruta
  balance = round2(balance + calc.grossRevenue)
  await sql`UPDATE economy_accounts SET balance_brl = ${balance}, updated_at = NOW() WHERE user_id = ${userId}`
  await sql`
    INSERT INTO economy_ledger(user_id, trip_id, entry_type, description, amount_brl, balance_after_brl, metadata)
    VALUES(${userId}, ${tripId}, 'trip_income',
           ${`Frete: ${trip.cargo ?? 'carga'} - ${calc.distanceKm} km`},
           ${calc.grossRevenue}, ${balance}, ${JSON.stringify({ cargoKey: key, ...calc })})`

  // Despesas da viagem
  if (totalExpenses > 0) {
    balance = round2(balance - totalExpenses)
    await sql`UPDATE economy_accounts SET balance_brl = ${balance}, updated_at = NOW() WHERE user_id = ${userId}`
    await sql`
      INSERT INTO economy_ledger(user_id, trip_id, entry_type, description, amount_brl, balance_after_brl, metadata)
      VALUES(${userId}, ${tripId}, 'trip_expenses',
             'Combustivel, manutencao e demais despesas da viagem',
             ${-totalExpenses}, ${balance},
             ${JSON.stringify({ fuelCost: round2(fuelExpense), maintenanceCost: round2(maintenanceExpense), totalExpenses })})`
  }

  // Parcela do empréstimo — descontada da receita líquida da viagem
  const netAfterExpenses = round2(calc.grossRevenue - totalExpenses)
  const loanRows = await sql`
    SELECT id, principal_brl, remaining_brl, repayment_pct, installments_total, installments_paid, installment_min_brl
      FROM economy_loans WHERE user_id = ${userId} AND status = 'active' ORDER BY created_at ASC LIMIT 1`
  const loan = loanRows[0]
  let loanPayment = 0

  if (loan && netAfterExpenses > 0) {
    const byPct = netAfterExpenses * num(loan.repayment_pct, 20) / 100
    const minInstallment = num(loan.installment_min_brl)
    const target = Math.max(byPct, Math.min(minInstallment, netAfterExpenses))
    loanPayment = round2(Math.min(num(loan.remaining_brl), target))

    if (loanPayment > 0) {
      const remaining = round2(Math.max(0, num(loan.remaining_brl) - loanPayment))
      const totalInstallments = num(loan.installments_total, 10)
      const paidCount = Math.min(totalInstallments, num(loan.installments_paid) + 1)
      balance = round2(balance - loanPayment)
      await sql`UPDATE economy_accounts SET balance_brl = ${balance}, updated_at = NOW() WHERE user_id = ${userId}`
      await sql`
        UPDATE economy_loans
           SET remaining_brl = ${remaining},
               installments_paid = ${paidCount},
               status = ${remaining <= 0 ? 'paid' : 'active'},
               paid_at = ${remaining <= 0 ? new Date().toISOString() : null}
         WHERE id = ${loan.id}`
      await sql`
        INSERT INTO economy_ledger(user_id, trip_id, entry_type, description, amount_brl, balance_after_brl, metadata)
        VALUES(${userId}, ${tripId}, 'loan_payment',
               ${`Parcela ${paidCount}/${totalInstallments} do emprestimo`},
               ${-loanPayment}, ${balance},
               ${JSON.stringify({ loanId: loan.id, repaymentPct: num(loan.repayment_pct), remainingBrl: remaining })})`
    }
  }

  return {
    alreadySettled: false,
    cargoKey: key,
    ...calc,
    totalExpenses,
    loanPayment,
    netBrl: round2(calc.grossRevenue - totalExpenses - loanPayment),
    openingBalance,
    balanceBrl: balance,
  }
}

/* ------------------------------------------------------------
   ROTAS
------------------------------------------------------------ */
export function registerEconomyRoutes(app: any) {
  const unauthorized = (c: any) => c.json({ ok: false, error: 'Sessão inválida ou expirada.' }, 401)

  /** Saldo, empréstimo ativo e extrato recente. */
  app.get('/me/economy', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const sql = neon(c.env.DATABASE_URL)
      await sql`INSERT INTO economy_accounts(user_id, balance_brl) VALUES(${user.id}, 0) ON CONFLICT(user_id) DO NOTHING`
      const account = await sql`SELECT balance_brl, updated_at FROM economy_accounts WHERE user_id = ${user.id}`
      const ledger = await sql`
        SELECT id, trip_id, entry_type, description, amount_brl, balance_after_brl, metadata, created_at
          FROM economy_ledger WHERE user_id = ${user.id} ORDER BY created_at DESC LIMIT 80`
      const loan = await sql`
        SELECT id, principal_brl, remaining_brl, repayment_pct, installments_total, installments_paid,
               installment_min_brl, status, created_at, paid_at
          FROM economy_loans WHERE user_id = ${user.id} AND status = 'active' LIMIT 1`
      const totals = await sql`
        SELECT
          COALESCE(SUM(amount_brl) FILTER (WHERE amount_brl > 0), 0) AS credits,
          COALESCE(SUM(-amount_brl) FILTER (WHERE amount_brl < 0), 0) AS debits,
          COUNT(*) FILTER (WHERE entry_type = 'trip_income')::int    AS trips
        FROM economy_ledger WHERE user_id = ${user.id}`
      return c.json({
        ok: true,
        account: { balanceBrl: round2(num(account[0]?.balance_brl)), updatedAt: account[0]?.updated_at ?? null },
        loan: loan[0] ?? null,
        totals: {
          creditsBrl: round2(num(totals[0]?.credits)),
          debitsBrl: round2(num(totals[0]?.debits)),
          trips: num(totals[0]?.trips),
        },
        ledger,
      }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('economy_load_error', error)
      return c.json({ ok: false, error: 'Erro ao carregar o banco.' }, 500)
    }
  })

  /** Livro-caixa — entradas e saídas agrupadas por dia. */
  app.get('/me/economy/cashbook', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const days = Math.min(180, Math.max(1, Math.trunc(num(c.req.query('days'), 30))))
      const sql = neon(c.env.DATABASE_URL)
      const entries = await sql`
        SELECT id, trip_id, entry_type, description, amount_brl, balance_after_brl, metadata, created_at
          FROM economy_ledger
         WHERE user_id = ${user.id} AND created_at >= NOW() - MAKE_INTERVAL(days => ${days})
         ORDER BY created_at DESC LIMIT 400`
      const daily = await sql`
        SELECT DATE(created_at) AS day,
               COALESCE(SUM(amount_brl) FILTER (WHERE amount_brl > 0), 0)  AS credits,
               COALESCE(SUM(-amount_brl) FILTER (WHERE amount_brl < 0), 0) AS debits,
               COALESCE(SUM(amount_brl), 0)                                AS result,
               COUNT(*)::int                                               AS movements
          FROM economy_ledger
         WHERE user_id = ${user.id} AND created_at >= NOW() - MAKE_INTERVAL(days => ${days})
         GROUP BY DATE(created_at) ORDER BY day DESC`
      const byType = await sql`
        SELECT entry_type, COALESCE(SUM(amount_brl), 0) AS total, COUNT(*)::int AS movements
          FROM economy_ledger
         WHERE user_id = ${user.id} AND created_at >= NOW() - MAKE_INTERVAL(days => ${days})
         GROUP BY entry_type ORDER BY entry_type`
      return c.json({ ok: true, days, entries, daily, byType }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('cashbook_error', error)
      return c.json({ ok: false, error: 'Erro ao carregar o livro-caixa.' }, 500)
    }
  })

  /** Tarifas por tipo de carga e parâmetros da economia. */
  app.get('/me/economy/rates', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const sql = neon(c.env.DATABASE_URL)
      const s = await loadSettings(sql)
      const rates = await sql`
        SELECT cargo_key, display_name, rate_brl_km, active
          FROM cargo_rates WHERE active = TRUE ORDER BY rate_brl_km`
      return c.json({
        ok: true,
        settings: {
          fuelPriceBrl: round2(num(s.fuel_price_brl)),
          minimumMarginPct: round2(num(s.minimum_margin_pct)),
          maintenanceBrlKm: round2(num(s.maintenance_brl_km)),
          efficiencyBonusPct: round2(num(s.efficiency_bonus_pct)),
          efficiencyTargetLKm: round2(num(s.efficiency_target_l_km)),
          cleanDeliveryBonusPct: round2(num(s.clean_delivery_bonus_pct)),
          damagePenaltyPct: round2(num(s.damage_penalty_pct)),
          weightSurchargeBrlTonKm: num(s.weight_surcharge_brl_ton_km),
          freeWeightTons: round2(num(s.free_weight_tons)),
          minimumTripRevenueBrl: round2(num(s.minimum_trip_revenue_brl)),
        },
        rates,
      }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('rates_error', error)
      return c.json({ ok: false, error: 'Erro ao carregar as tarifas.' }, 500)
    }
  })

  /** Empréstimo inicial de R$ 5.000 ou R$ 10.000. */
  app.post('/me/economy/loan', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const body = await c.req.json().catch(() => null) as any
      const principal = num(body?.principalBrl)
      const pct = num(body?.repaymentPct, 20)
      const installments = Math.min(24, Math.max(4, Math.trunc(num(body?.installments, 10))))

      if (!LOAN_OPTIONS.includes(principal))
        return c.json({ ok: false, error: 'Empréstimo inicial disponível em R$ 5.000 ou R$ 10.000.' }, 400)
      if (pct < 10 || pct > 50)
        return c.json({ ok: false, error: 'A parcela deve ficar entre 10% e 50% da receita líquida.' }, 400)

      const sql = neon(c.env.DATABASE_URL)
      const existing = await sql`SELECT id FROM economy_loans WHERE user_id = ${user.id} AND status = 'active' LIMIT 1`
      if (existing[0]) return c.json({ ok: false, error: 'Você já possui um empréstimo ativo.' }, 409)

      const installmentMin = round2(principal / installments)
      await sql`INSERT INTO economy_accounts(user_id, balance_brl) VALUES(${user.id}, 0) ON CONFLICT(user_id) DO NOTHING`
      const account = await sql`SELECT balance_brl FROM economy_accounts WHERE user_id = ${user.id}`
      const balance = round2(num(account[0]?.balance_brl) + principal)

      const loan = await sql`
        INSERT INTO economy_loans(user_id, principal_brl, remaining_brl, repayment_pct, installments_total, installment_min_brl)
        VALUES(${user.id}, ${principal}, ${principal}, ${pct}, ${installments}, ${installmentMin})
        RETURNING id, principal_brl, remaining_brl, repayment_pct, installments_total, installments_paid, installment_min_brl, status, created_at`

      await sql`UPDATE economy_accounts SET balance_brl = ${balance}, updated_at = NOW() WHERE user_id = ${user.id}`
      await sql`
        INSERT INTO economy_ledger(user_id, entry_type, description, amount_brl, balance_after_brl, metadata)
        VALUES(${user.id}, 'loan_credit',
               ${`Emprestimo inicial em ${installments} parcelas`},
               ${principal}, ${balance}, ${JSON.stringify({ loanId: loan[0].id, installments, installmentMin, repaymentPct: pct })})`

      return c.json({ ok: true, loan: loan[0], balanceBrl: balance }, 201)
    } catch (error) {
      console.error('loan_error', error)
      return c.json({ ok: false, error: 'Erro ao processar o empréstimo.' }, 500)
    }
  })

  /** Quitação antecipada do empréstimo. */
  app.post('/me/economy/loan/settle', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const sql = neon(c.env.DATABASE_URL)
      const loanRows = await sql`SELECT id, remaining_brl FROM economy_loans WHERE user_id = ${user.id} AND status = 'active' LIMIT 1`
      const loan = loanRows[0]
      if (!loan) return c.json({ ok: false, error: 'Nenhum empréstimo ativo.' }, 404)

      const account = await sql`SELECT balance_brl FROM economy_accounts WHERE user_id = ${user.id}`
      const balance = round2(num(account[0]?.balance_brl))
      const remaining = round2(num(loan.remaining_brl))
      if (balance < remaining)
        return c.json({ ok: false, error: `Saldo insuficiente. Faltam R$ ${round2(remaining - balance)}.` }, 400)

      const newBalance = round2(balance - remaining)
      await sql`UPDATE economy_accounts SET balance_brl = ${newBalance}, updated_at = NOW() WHERE user_id = ${user.id}`
      await sql`UPDATE economy_loans SET remaining_brl = 0, status = 'paid', paid_at = NOW() WHERE id = ${loan.id}`
      await sql`
        INSERT INTO economy_ledger(user_id, entry_type, description, amount_brl, balance_after_brl, metadata)
        VALUES(${user.id}, 'loan_settlement', 'Quitacao antecipada do emprestimo',
               ${-remaining}, ${newBalance}, ${JSON.stringify({ loanId: loan.id })})`
      return c.json({ ok: true, balanceBrl: newBalance })
    } catch (error) {
      console.error('loan_settle_error', error)
      return c.json({ ok: false, error: 'Erro ao quitar o empréstimo.' }, 500)
    }
  })

  /** Simulação da viagem em andamento — usa os litros da telemetria. */
  app.get('/me/trips/:id/economy-preview', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    const id = c.req.param('id')
    if (!UUID_RE.test(id)) return c.json({ ok: false, error: 'Viagem não encontrada.' }, 404)
    try {
      const sql = neon(c.env.DATABASE_URL)
      const rows = await sql`
        SELECT id, cargo, cargo_mass_kg, distance_km, fuel_used_l, cargo_damage, status
          FROM trips WHERE id = ${id} AND user_id = ${user.id} LIMIT 1`
      const trip = rows[0]
      if (!trip) return c.json({ ok: false, error: 'Viagem não encontrada.' }, 404)

      // Viagem ativa ainda não tem distance_km/fuel_used_l: deriva da telemetria.
      let distance = num(trip.distance_km)
      let fuel = num(trip.fuel_used_l)
      let damage = num(trip.cargo_damage)

      if (trip.status === 'active') {
        const sample = await sql`
          SELECT MIN(odometer_km) AS start_odo, MAX(odometer_km) AS end_odo,
                 MIN(fuel_l) AS min_fuel, MAX(fuel_l) AS max_fuel,
                 MAX(cargo_damage) AS damage, COUNT(*)::int AS count
            FROM trip_telemetry_samples WHERE trip_id = ${id}`
        if (num(sample[0]?.count) > 0) {
          distance = Math.max(0, num(sample[0].end_odo) - num(sample[0].start_odo))
          fuel = Math.max(0, num(sample[0].max_fuel) - num(sample[0].min_fuel))
          damage = num(sample[0].damage)
        }
      }

      const settings = await loadSettings(sql)
      const key = cargoKey(trip.cargo)
      const rate = await loadRate(sql, key)
      const calc = calculateTripEconomy({
        distanceKm: distance, fuelLiters: fuel, massKg: num(trip.cargo_mass_kg),
        cargoDamage: damage, rateBrlKm: rate, settings,
      })

      return c.json({
        ok: true,
        preview: { cargo: trip.cargo, cargoKey: key, status: trip.status, ...calc },
      }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('preview_error', error)
      return c.json({ ok: false, error: 'Erro ao simular a viagem.' }, 500)
    }
  })
}
