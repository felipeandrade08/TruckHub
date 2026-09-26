import { neon } from '@neondatabase/serverless'
import { hashSessionToken, getCookie } from './sharedAuth'

/* ============================================================
   TransPoli — GARAGEM EXCLUSIVA
   ------------------------------------------------------------
   Um caminhão vinculado a um motorista só pode ser operado por
   ele. A autorização é consultada pelo tablet a cada poucos
   segundos; qualquer resposta "authorized: false" aciona o
   bloqueio físico no desktop.

   Regra de decisão em /me/garage/authorize:

     - Motorista sem nenhum vínculo exclusivo  -> libera (configured: false)
     - Caminhão bate com um vínculo do motorista -> libera
     - Caminhão pertence a OUTRO motorista       -> bloqueia (foreign_truck)
     - Motorista tem garagem mas o caminhão não  -> bloqueia (not_in_garage)
       está nela
   ============================================================ */

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

function clean(value: any, max = 120) {
  return String(value ?? '').trim().slice(0, max)
}

function normalizePart(value: any) {
  return String(value ?? '')
    .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
    .trim().toLowerCase().replace(/\s+/g, ' ')
}

function normalizePlate(value: any) {
  return String(value ?? '').toUpperCase().replace(/[^A-Z0-9]/g, '')
}

/** Chave estável do caminhão: marca|modelo|placa, normalizando pontuação da placa. */
export function buildTruckKey(brand: any, model: any, plate: any) {
  return normalizePart(brand) + '|' + normalizePart(model) + '|' + normalizePlate(plate)
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

async function logAccess(
  sql: any, userId: string, key: string,
  brand: string, model: string, plate: string,
  authorized: boolean, reason: string,
) {
  try {
    // Evita encher a tabela: só grava quando o resultado muda.
    const last = await sql`
      SELECT authorized, reason FROM garage_access_log
       WHERE user_id = ${userId} AND truck_key = ${key}
       ORDER BY created_at DESC LIMIT 1`
    if (last[0] && last[0].authorized === authorized && last[0].reason === reason) return
    await sql`
      INSERT INTO garage_access_log(user_id, truck_key, brand, model, license_plate, authorized, reason)
      VALUES(${userId}, ${key}, ${brand}, ${model}, ${plate}, ${authorized}, ${reason})`
  } catch (error) {
    // O log nunca pode derrubar a autorização, mas a falha precisa ficar diagnosticável.
    console.error('garage_access_log_error', error)
  }
}

export function registerGarageRoutes(app: any) {
  const unauthorized = (c: any) => c.json({ ok: false, error: 'Sessão inválida ou expirada.' }, 401)

  /** Lista a garagem do motorista. */
  app.get('/me/garage', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const sql = neon(c.env.DATABASE_URL)
      const rows = await sql`
        SELECT g.id, g.truck_id, g.exclusive, g.active, g.skin_code, g.label,
               g.truck_key, g.last_seen_at, g.created_at,
               t.truck_name, t.brand, t.model, t.license_plate
          FROM garage_assignments g
          JOIN trucks t ON t.id = g.truck_id
         WHERE g.user_id = ${user.id}
         ORDER BY g.created_at DESC`
      return c.json({ ok: true, garage: rows }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('garage_list_error', error)
      return c.json({ ok: false, error: 'Erro ao carregar a garagem.' }, 500)
    }
  })

  /**
   * Sincroniza o inventário de caminhões legível do save do ETS2.
   * A operação é aditiva: nunca remove vínculos existentes quando o save
   * está parcial/protegido.
   */
  app.post('/me/garage/sync-save', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const body = await c.req.json().catch(() => null) as any
      const trucks = Array.isArray(body?.trucks) ? body.trucks.slice(0, 100) : []
      if (!trucks.length) return c.json({ ok: true, received: 0, created: 0, alreadyBound: 0, skipped: 0 })
      const sql = neon(c.env.DATABASE_URL)
      let created = 0, alreadyBound = 0, skipped = 0
      for (const item of trucks) {
        const brand = clean(item?.brand, 80)
        const model = clean(item?.model, 120)
        const plate = clean(item?.plate ?? item?.licensePlate, 32)
        const label = clean(item?.label, 160) || `${brand} ${model}`.trim() || 'Caminhão'
        if (!brand && !model) { skipped++; continue }
        const key = buildTruckKey(brand, model, plate)
        const foreign = await sql`
          SELECT id FROM garage_assignments
           WHERE truck_key = ${key} AND user_id <> ${user.id} AND active = TRUE LIMIT 1`
        if (foreign[0]) { skipped++; continue }
        const mine = await sql`
          SELECT id FROM garage_assignments
           WHERE truck_key = ${key} AND user_id = ${user.id} LIMIT 1`
        if (mine[0]) {
          await sql`UPDATE garage_assignments SET active = TRUE, last_seen_at = NOW(), updated_at = NOW() WHERE id = ${mine[0].id}`
          alreadyBound++
          continue
        }
        const truckRows = await sql`
          SELECT id FROM trucks
           WHERE user_id = ${user.id}
             AND LOWER(COALESCE(brand,'')) = LOWER(${brand})
             AND LOWER(COALESCE(model,'')) = LOWER(${model})
             AND LOWER(COALESCE(license_plate,'')) = LOWER(${plate})
           LIMIT 1`
        let truckId = truckRows[0]?.id
        if (!truckId) {
          const createdTruck = await sql`
            INSERT INTO trucks(user_id, truck_name, brand, model, license_plate)
            VALUES(${user.id}, ${label}, ${brand}, ${model}, ${plate})
            RETURNING id`
          truckId = createdTruck[0].id
        }
        await sql`
          INSERT INTO garage_assignments(user_id, truck_id, exclusive, label, truck_key, active, last_seen_at)
          VALUES(${user.id}, ${truckId}, TRUE, ${label}, ${key}, TRUE, NOW())`
        created++
      }
      return c.json({ ok: true, received: trucks.length, created, alreadyBound, skipped })
    } catch (error) {
      console.error('garage_save_sync_error', error)
      return c.json({ ok: false, error: 'Erro ao sincronizar os caminhões do save.' }, 500)
    }
  })
  /**
   * Vincula o caminhão que está na telemetria agora.
   * Cria o registro em `trucks` se ainda não existir — é assim que o
   * motorista cadastra o caminhão direto pelo tablet.
   */
  app.post('/me/garage/bind-current', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const body = await c.req.json().catch(() => null) as any
      const brand = clean(body?.brand, 80)
      const model = clean(body?.model, 120)
      const plate = clean(body?.plate ?? body?.licensePlate, 32)
      const label = clean(body?.label, 160) || null
      const skin = clean(body?.skinCode, 120) || null

      if (!brand && !model)
        return c.json({ ok: false, error: 'A telemetria ainda não informou o caminhão.' }, 400)

      const key = buildTruckKey(brand, model, plate)
      const sql = neon(c.env.DATABASE_URL)

      // Já vinculado a outro motorista?
      const foreign = await sql`
        SELECT id FROM garage_assignments
         WHERE truck_key = ${key} AND user_id <> ${user.id} AND active = TRUE LIMIT 1`
      if (foreign[0])
        return c.json({ ok: false, error: 'Este caminhão já pertence a outro motorista.' }, 409)

      // Já está na garagem deste motorista?
      const mine = await sql`
        SELECT id FROM garage_assignments WHERE truck_key = ${key} AND user_id = ${user.id} LIMIT 1`
      if (mine[0]) {
        await sql`UPDATE garage_assignments SET active = TRUE, last_seen_at = NOW(), updated_at = NOW() WHERE id = ${mine[0].id}`
        return c.json({ ok: true, alreadyBound: true, assignmentId: mine[0].id })
      }

      // Reaproveita ou cria o caminhão do motorista.
      const truckRows = await sql`
        SELECT id FROM trucks
         WHERE user_id = ${user.id}
           AND LOWER(COALESCE(brand,'')) = LOWER(${brand})
           AND LOWER(COALESCE(model,'')) = LOWER(${model})
           AND LOWER(COALESCE(license_plate,'')) = LOWER(${plate})
         LIMIT 1`
      let truckId = truckRows[0]?.id
      if (!truckId) {
        const created = await sql`
          INSERT INTO trucks(user_id, truck_name, brand, model, license_plate)
          VALUES(${user.id}, ${label ?? `${brand} ${model}`.trim()}, ${brand}, ${model}, ${plate})
          RETURNING id`
        truckId = created[0].id
      }

      const assignment = await sql`
        INSERT INTO garage_assignments(user_id, truck_id, exclusive, skin_code, label, truck_key, active, last_seen_at)
        VALUES(${user.id}, ${truckId}, TRUE, ${skin}, ${label}, ${key}, TRUE, NOW())
        RETURNING id, truck_id, exclusive, active, skin_code, label, truck_key`

      return c.json({ ok: true, assignment: assignment[0] }, 201)
    } catch (error) {
      console.error('garage_bind_error', error)
      return c.json({ ok: false, error: 'Erro ao vincular o caminhão.' }, 500)
    }
  })

  /** Vínculo manual por truck_id já cadastrado. */
  app.post('/me/garage', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const body = await c.req.json().catch(() => null) as any
      const truckId = String(body?.truckId ?? '')
      if (!UUID_RE.test(truckId)) return c.json({ ok: false, error: 'Caminhão inválido.' }, 400)

      const sql = neon(c.env.DATABASE_URL)
      const truck = await sql`
        SELECT id, brand, model, license_plate FROM trucks
         WHERE id = ${truckId} AND user_id = ${user.id} LIMIT 1`
      if (!truck[0]) return c.json({ ok: false, error: 'O caminhão não pertence a este motorista.' }, 404)

      const existing = await sql`SELECT id FROM garage_assignments WHERE truck_id = ${truckId} LIMIT 1`
      if (existing[0]) return c.json({ ok: false, error: 'Este caminhão já está vinculado.' }, 409)

      const key = buildTruckKey(truck[0].brand, truck[0].model, truck[0].license_plate)
      const row = await sql`
        INSERT INTO garage_assignments(user_id, truck_id, exclusive, skin_code, label, truck_key, active)
        VALUES(${user.id}, ${truckId}, ${body?.exclusive !== false}, ${body?.skinCode ?? null}, ${body?.label ?? null}, ${key}, TRUE)
        RETURNING id, truck_id, exclusive, active, skin_code, label, truck_key`
      return c.json({ ok: true, assignment: row[0] }, 201)
    } catch (error) {
      console.error('garage_create_error', error)
      return c.json({ ok: false, error: 'Erro ao vincular o caminhão.' }, 500)
    }
  })

  /** Remove um vínculo. */
  app.delete('/me/garage/:id', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    const id = c.req.param('id')
    if (!UUID_RE.test(id)) return c.json({ ok: false, error: 'Vínculo não encontrado.' }, 404)
    try {
      const sql = neon(c.env.DATABASE_URL)
      const row = await sql`DELETE FROM garage_assignments WHERE id = ${id} AND user_id = ${user.id} RETURNING id`
      if (!row[0]) return c.json({ ok: false, error: 'Vínculo não encontrado.' }, 404)
      return c.json({ ok: true })
    } catch (error) {
      console.error('garage_delete_error', error)
      return c.json({ ok: false, error: 'Erro ao remover o vínculo.' }, 500)
    }
  })

  /** Decisão de bloqueio consultada pelo tablet. */
  app.get('/me/garage/authorize', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const brand = clean(c.req.query('brand'), 80)
      const model = clean(c.req.query('model'), 120)
      const plate = clean(c.req.query('plate'), 32)
      const key = buildTruckKey(brand, model, plate)
      const sql = neon(c.env.DATABASE_URL)

      // A identidade usada na decisão vem da telemetria persistida mais recente.
      // Os parâmetros do desktop são apenas uma cópia da mesma leitura; eles
      // nunca podem, sozinhos, autorizar um caminhão.
      const live = await sql`
        SELECT truck_brand, truck_model, license_plate, source_city, destination_city,
               recorded_at, connected
          FROM device_telemetry_latest
         WHERE user_id = ${user.id}
         LIMIT 1
      `
      const telemetry = live[0]
      const telemetryAgeMs = telemetry?.recorded_at
        ? Date.now() - new Date(telemetry.recorded_at).getTime()
        : Number.POSITIVE_INFINITY

      if (!telemetry || !telemetry.connected || !Number.isFinite(telemetryAgeMs) || telemetryAgeMs > 30000) {
        await logAccess(sql, user.id, key, brand, model, plate, false, 'telemetry_unavailable')
        return c.json({
          ok: true, configured: true, authorized: false, reason: 'telemetry_unavailable',
          message: 'Aguardando telemetria recente do ETS2 para confirmar o caminhão.',
          telemetryAt: telemetry?.recorded_at ?? null,
          sourceCity: telemetry?.source_city ?? null,
          destinationCity: telemetry?.destination_city ?? null,
        }, { headers: { 'Cache-Control': 'no-store' } })
      }

      const liveBrand = clean(telemetry.truck_brand, 80)
      const liveModel = clean(telemetry.truck_model, 120)
      const livePlate = clean(telemetry.license_plate, 32)
      const liveKey = buildTruckKey(liveBrand, liveModel, livePlate)

      if (!liveBrand && !liveModel) {
        await logAccess(sql, user.id, liveKey || key, liveBrand, liveModel, livePlate, false, 'telemetry_unavailable')
        return c.json({
          ok: true, configured: true, authorized: false, reason: 'telemetry_unavailable',
          message: 'A telemetria recente ainda não identificou o caminhão.',
          telemetryAt: telemetry.recorded_at,
          sourceCity: telemetry.source_city ?? null,
          destinationCity: telemetry.destination_city ?? null,
        }, { headers: { 'Cache-Control': 'no-store' } })
      }

      const samePart = (a: string, b: string) => normalizePart(a) === normalizePart(b)
      const samePlate = !livePlate || !plate || normalizePlate(livePlate) === normalizePlate(plate)
      if (!samePart(liveBrand, brand) || !samePart(liveModel, model) || !samePlate) {
        await logAccess(sql, user.id, liveKey, liveBrand, liveModel, livePlate, false, 'telemetry_identity_mismatch')
        return c.json({
          ok: true, configured: true, authorized: false, reason: 'telemetry_identity_mismatch',
          message: 'A identidade enviada pelo aplicativo não corresponde à telemetria recente do ETS2.',
          truckKey: liveKey,
          telemetryAt: telemetry.recorded_at,
          sourceCity: telemetry.source_city ?? null,
          destinationCity: telemetry.destination_city ?? null,
        }, { headers: { 'Cache-Control': 'no-store' } })
      }

      // A partir daqui, a chave confiável é a própria telemetria.
      const effectiveBrand = liveBrand
      const effectiveModel = liveModel
      const effectivePlate = livePlate
      const effectiveKey = liveKey

      const total = await sql`
        SELECT COUNT(*)::int AS count FROM garage_assignments
         WHERE user_id = ${user.id} AND exclusive = TRUE AND active = TRUE`
      if (Number(total[0]?.count || 0) === 0)
        return c.json({ ok: true, configured: false, authorized: true, reason: 'garage_empty' },
          { headers: { 'Cache-Control': 'no-store' } })

      // Vínculo exato deste motorista.
      const mine = await sql`
        SELECT g.id, g.label, g.skin_code, t.truck_name, t.brand, t.model, t.license_plate
          FROM garage_assignments g
          JOIN trucks t ON t.id = g.truck_id
         WHERE g.user_id = ${user.id} AND g.exclusive = TRUE AND g.active = TRUE
           AND g.truck_key = ${effectiveKey}
         LIMIT 1`

      if (mine[0]) {
        await sql`UPDATE garage_assignments SET last_seen_at = NOW() WHERE id = ${mine[0].id}`
        await logAccess(sql, user.id, effectiveKey, effectiveBrand, effectiveModel, effectivePlate, true, 'authorized')
        return c.json({ ok: true, configured: true, authorized: true, reason: 'authorized', assignment: mine[0] },
          { headers: { 'Cache-Control': 'no-store' } })
      }

      // Compatibilidade com vínculos antigos cuja chave ainda usava a placa formatada.
      const legacyMine = await sql`
        SELECT g.id
          FROM garage_assignments g
          JOIN trucks t ON t.id = g.truck_id
         WHERE g.user_id = ${user.id} AND g.exclusive = TRUE AND g.active = TRUE
           AND LOWER(COALESCE(t.brand,'')) = LOWER(${effectiveBrand})
           AND LOWER(COALESCE(t.model,'')) = LOWER(${effectiveModel})
           AND UPPER(REGEXP_REPLACE(COALESCE(t.license_plate,''), '[^A-Za-z0-9]', '', 'g')) = ${normalizePlate(effectivePlate)}
         LIMIT 1`
      if (legacyMine[0]) {
        await sql`UPDATE garage_assignments SET truck_key = ${effectiveKey}, last_seen_at = NOW() WHERE id = ${legacyMine[0].id}`
        await logAccess(sql, user.id, effectiveKey, effectiveBrand, effectiveModel, effectivePlate, true, 'authorized')
        return c.json({ ok: true, configured: true, authorized: true, reason: 'authorized', assignmentId: legacyMine[0].id, repairedKey: true },
          { headers: { 'Cache-Control': 'no-store' } })
      }
      // Pertence a outro motorista?
      const foreign = await sql`
        SELECT id FROM garage_assignments
         WHERE truck_key = ${effectiveKey} AND user_id <> ${user.id} AND active = TRUE LIMIT 1`
      const reason = foreign[0] ? 'foreign_truck' : 'not_in_garage'
      await logAccess(sql, user.id, effectiveKey, effectiveBrand, effectiveModel, effectivePlate, false, reason)

      return c.json({
        ok: true, configured: true, authorized: false, reason,
        message: foreign[0]
          ? 'Este caminhão está vinculado a outro motorista na garagem TransPoli.'
          : 'Este caminhão não está na sua garagem exclusiva. Vincule-o pelo tablet para liberar.',
        truckKey: effectiveKey,
      }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('garage_authorize_error', error)
      // Falha de servidor não pode liberar um caminhão por engano.
      return c.json({
        ok: false, configured: true, authorized: false, reason: 'server_error',
        message: 'Não foi possível confirmar a garagem pela telemetria agora.'
      }, 503)
    }
  })

  /** Histórico de tentativas de acesso. */
  app.get('/me/garage/log', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return unauthorized(c)
    try {
      const sql = neon(c.env.DATABASE_URL)
      const rows = await sql`
        SELECT id, truck_key, brand, model, license_plate, authorized, reason, created_at
          FROM garage_access_log WHERE user_id = ${user.id}
         ORDER BY created_at DESC LIMIT 50`
      return c.json({ ok: true, log: rows }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('garage_log_error', error)
      return c.json({ ok: false, error: 'Erro ao carregar o histórico.' }, 500)
    }
  })
}
