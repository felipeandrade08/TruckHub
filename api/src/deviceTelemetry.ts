import { neon } from '@neondatabase/serverless'

const enc = new TextEncoder()
const DEVICE_ID_MAX = 255
const MAX_SPEED = 250
const MAX_RPM = 10000
const MAX_FUEL = 2000
const MAX_RANGE = 10000
const MAX_ODOMETER = 10000000

function b64(bytes: Uint8Array) {
  let s = ''
  for (const b of bytes) s += String.fromCharCode(b)
  return btoa(s)
}

async function hash(value: string) {
  return b64(new Uint8Array(await crypto.subtle.digest('SHA-256', enc.encode(value))))
}

function cookie(request: Request, name: string) {
  const raw = request.headers.get('Cookie') ?? ''
  for (const part of raw.split(';')) {
    const [key, ...value] = part.trim().split('=')
    if (key === name) return value.join('=')
  }
  return null
}

async function currentUser(c: any) {
  if (!c.env.DATABASE_URL) return null
  const bearer = c.req.header('Authorization')?.replace(/^Bearer\s+/i, '').trim()
  const token = bearer || cookie(c.req.raw, 'truckhub_session')
  if (!token) return null
  const sql = neon(c.env.DATABASE_URL)
  const tokenHash = await hash(token)
  const rows = await sql`
    SELECT u.id, u.name, u.email
    FROM sessions s
    JOIN users u ON u.id = s.user_id
    WHERE s.token_hash = ${tokenHash}
      AND s.revoked_at IS NULL
      AND s.expires_at > NOW()
      AND u.status = 'active'
      AND s.session_type IN ('web','desktop')
    LIMIT 1
  `
  return rows[0] ?? null
}

function numberOr(value: any, fallback = 0, min = -Infinity, max = Infinity) {
  const n = Number(value)
  return Number.isFinite(n) && n >= min && n <= max ? n : fallback
}

function bool(value: any) { return Boolean(value) }
function text(value: any, max: number) {
  if (value == null) return null
  const v = String(value).trim()
  return v ? v.slice(0, max) : null
}

function output(row: any) {
  if (!row) return null
  return {
    recordedAt: row.recorded_at,
    deviceId: row.device_id,
    connected: Boolean(row.connected),
    game: row.game,
    gamePaused: Boolean(row.game_paused),
    engineEnabled: Boolean(row.engine_enabled),
    electricEnabled: Boolean(row.electric_enabled),
    speedKph: Number(row.speed_kph) || 0,
    speedLimitKph: Number(row.speed_limit_kph) || 0,
    rpm: Number(row.rpm) || 0,
    gear: Number(row.gear) || 0,
    fuelL: Number(row.fuel_l) || 0,
    fuelRangeKm: Number(row.fuel_range_km) || 0,
    fuelAvgConsumption: Number(row.fuel_avg_consumption) || 0,
    adblueL: Number(row.adblue_l) || 0,
    oilPressure: Number(row.oil_pressure) || 0,
    oilTemperature: Number(row.oil_temperature) || 0,
    waterTemperature: Number(row.water_temperature) || 0,
    batteryVoltage: Number(row.battery_voltage) || 0,
    odometerKm: Number(row.odometer_km) || 0,\n    worldX: Number(row.world_x) || 0,\n    worldY: Number(row.world_y) || 0,\n    worldZ: Number(row.world_z) || 0,\n    headingDeg: Number(row.heading_deg) || 0,\n    pitchDeg: Number(row.pitch_deg) || 0,\n    rollDeg: Number(row.roll_deg) || 0,\n    positionValid: Boolean(row.position_valid),
    airPressure: Number(row.air_pressure) || 0,
    brakeTemperature: Number(row.brake_temperature) || 0,
    parkingBrake: Boolean(row.parking_brake),
    motorBrake: Boolean(row.motor_brake),
    brakeLight: Boolean(row.brake_light),
    cruiseControl: Boolean(row.cruise_control),
    cruiseSpeedKph: Number(row.cruise_speed_kph) || 0,
    retarderLevel: Number(row.retarder_level) || 0,
    userThrottle: Number(row.user_throttle) || 0,
    effectiveThrottle: Number(row.effective_throttle) || 0,
    userBrake: Number(row.user_brake) || 0,
    effectiveBrake: Number(row.effective_brake) || 0,
    wearEngine: Number(row.wear_engine) || 0,
    wearTransmission: Number(row.wear_transmission) || 0,
    wearCabin: Number(row.wear_cabin) || 0,
    wearChassis: Number(row.wear_chassis) || 0,
    wearWheels: Number(row.wear_wheels) || 0,
    cargoDamage: Number(row.cargo_damage) || 0,
    airPressureWarning: Boolean(row.air_pressure_warning),
    airPressureEmergency: Boolean(row.air_pressure_emergency),
    fuelWarning: Boolean(row.fuel_warning),
    adblueWarning: Boolean(row.adblue_warning),
    oilPressureWarning: Boolean(row.oil_pressure_warning),
    waterTemperatureWarning: Boolean(row.water_temperature_warning),
    batteryVoltageWarning: Boolean(row.battery_voltage_warning),
    wipers: Boolean(row.wipers),
    blinkerLeftActive: Boolean(row.blinker_left_active),
    blinkerRightActive: Boolean(row.blinker_right_active),
    lightsParking: Boolean(row.lights_parking),
    lightsBrake: Boolean(row.lights_brake),
    lightsReverse: Boolean(row.lights_reverse),
    lightsHazard: Boolean(row.lights_hazard),
    differentialLock: Boolean(row.differential_lock),
    liftAxle: Boolean(row.lift_axle),
    trailerLiftAxle: Boolean(row.trailer_lift_axle),
    truckBrand: row.truck_brand,
    truckModel: row.truck_model,
    licensePlate: row.license_plate,
    cargo: row.cargo,
    cargoMassKg: Number(row.cargo_mass_kg) || 0,
    sourceCity: row.source_city,
    destinationCity: row.destination_city,
    sourceCompany: row.source_company,
    destinationCompany: row.destination_company,
    plannedDistanceKm: Number(row.planned_distance_km) || 0,
    cargoValueBrl: row.cargo_value_brl == null ? null : Number(row.cargo_value_brl),
    onJob: Boolean(row.on_job),
    specialJob: Boolean(row.special_job),
    refuelActive: Boolean(row.refuel_active),
  }
}

export function registerDeviceTelemetryRoutes(app: any) {
  app.get('/me/device/telemetry', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return c.json({ ok: false, error: 'Sessão inválida ou expirada.' }, 401)
    try {
      const sql = neon(c.env.DATABASE_URL)
      const rows = await sql`SELECT * FROM device_telemetry_latest WHERE user_id = ${user.id} LIMIT 1`
      return c.json({ ok: true, telemetry: output(rows[0] ?? null) }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('device_telemetry_get_error', error)
      return c.json({ ok: false, error: 'Erro ao consultar a telemetria ao vivo.' }, 500)
    }
  })

  app.post('/me/device/telemetry', async (c: any) => {
    const user = await currentUser(c)
    if (!user) return c.json({ ok: false, error: 'Sessão inválida ou expirada.' }, 401)
    try {
      const data = await c.req.json().catch(() => null) as any
      const deviceId = text(data?.deviceId, DEVICE_ID_MAX)
      if (!deviceId || deviceId.length < 16) return c.json({ ok: false, error: 'Dispositivo inválido.' }, 400)
      const sql = neon(c.env.DATABASE_URL)
      const bound = await sql`
        SELECT d.id
        FROM devices d
        JOIN licenses l ON l.id = d.license_id
        WHERE l.user_id = ${user.id}
          AND d.device_id = ${deviceId}
          AND d.status = 'active'
        LIMIT 1
      `
      if (!bound[0]) return c.json({ ok: false, code: 'DEVICE_NOT_BOUND', error: 'Este dispositivo não está vinculado à licença.' }, 403)

      const recordedAt = data?.recordedAt ? new Date(data.recordedAt) : new Date()
      if (Number.isNaN(recordedAt.getTime()) || recordedAt.getTime() > Date.now() + 5 * 60 * 1000 || recordedAt.getTime() < Date.now() - 10 * 60 * 1000)
        return c.json({ ok: false, error: 'Data da telemetria inválida.' }, 400)

      const speed = numberOr(data?.speedKph, 0, 0, MAX_SPEED)
      const rpm = numberOr(data?.rpm, 0, 0, MAX_RPM)
      const fuel = numberOr(data?.fuelL, 0, 0, MAX_FUEL)
      const range = numberOr(data?.fuelRangeKm, 0, 0, MAX_RANGE)
      const odometer = numberOr(data?.odometerKm, 0, 0, MAX_ODOMETER)
      const game = text(data?.game, 20)

      await sql`
        INSERT INTO device_telemetry_latest (
          user_id,device_id,recorded_at,connected,game,game_paused,engine_enabled,electric_enabled,
          speed_kph,speed_limit_kph,rpm,gear,fuel_l,fuel_range_km,fuel_avg_consumption,adblue_l,
          oil_pressure,oil_temperature,water_temperature,battery_voltage,odometer_km,air_pressure,
          brake_temperature,parking_brake,motor_brake,brake_light,cruise_control,cruise_speed_kph,
          retarder_level,user_throttle,effective_throttle,user_brake,effective_brake,wear_engine,
          wear_transmission,wear_cabin,wear_chassis,wear_wheels,cargo_damage,air_pressure_warning,
          air_pressure_emergency,fuel_warning,adblue_warning,oil_pressure_warning,water_temperature_warning,
          battery_voltage_warning,wipers,blinker_left_active,blinker_right_active,lights_parking,lights_brake,
          lights_reverse,lights_hazard,differential_lock,lift_axle,trailer_lift_axle,truck_brand,truck_model,
          license_plate,cargo,cargo_mass_kg,source_city,destination_city,source_company,destination_company,
          planned_distance_km,cargo_value_brl,on_job,special_job,refuel_active
        ) VALUES (
          ${user.id},${deviceId},${recordedAt.toISOString()},${bool(data?.connected)},${game},${bool(data?.gamePaused)},${bool(data?.engineEnabled)},${bool(data?.electricEnabled)},
          ${speed},${numberOr(data?.speedLimitKph)},${rpm},${Math.trunc(numberOr(data?.gear,0,-10,20))},${fuel},${range},${numberOr(data?.fuelAvgConsumption)},${numberOr(data?.adblueL)},
          ${numberOr(data?.oilPressure)},${numberOr(data?.oilTemperature)},${numberOr(data?.waterTemperature)},${numberOr(data?.batteryVoltage)},${odometer},${numberOr(data?.airPressure)},
          ${numberOr(data?.brakeTemperature)},${bool(data?.parkingBrake)},${bool(data?.motorBrake)},${bool(data?.brakeLight)},${bool(data?.cruiseControl)},${numberOr(data?.cruiseSpeedKph)},
          ${Math.trunc(numberOr(data?.retarderLevel,0,0,20))},${numberOr(data?.userThrottle,0,0,1)},${numberOr(data?.effectiveThrottle,0,0,1)},${numberOr(data?.userBrake,0,0,1)},${numberOr(data?.effectiveBrake,0,0,1)},${numberOr(data?.wearEngine,0,0,1)},
          ${numberOr(data?.wearTransmission,0,0,1)},${numberOr(data?.wearCabin,0,0,1)},${numberOr(data?.wearChassis,0,0,1)},${numberOr(data?.wearWheels,0,0,1)},${numberOr(data?.cargoDamage,0,0,1)},${bool(data?.airPressureWarning)},
          ${bool(data?.airPressureEmergency)},${bool(data?.fuelWarning)},${bool(data?.adblueWarning)},${bool(data?.oilPressureWarning)},${bool(data?.waterTemperatureWarning)},
          ${bool(data?.batteryVoltageWarning)},${bool(data?.wipers)},${bool(data?.blinkerLeftActive)},${bool(data?.blinkerRightActive)},${bool(data?.lightsParking)},${bool(data?.lightsBrake)},
          ${bool(data?.lightsReverse)},${bool(data?.lightsHazard)},${bool(data?.differentialLock)},${bool(data?.liftAxle)},${bool(data?.trailerLiftAxle)},${text(data?.truckBrand,100)},${text(data?.truckModel,120)},
          ${text(data?.licensePlate,32)},${text(data?.cargo,180)},${numberOr(data?.cargoMassKg)},${text(data?.sourceCity,180)},${text(data?.destinationCity,180)},${text(data?.sourceCompany,180)},${text(data?.destinationCompany,180)},
          ${numberOr(data?.plannedDistanceKm)},${data?.cargoValueBrl == null ? null : numberOr(data?.cargoValueBrl,0,0,100000000)},${bool(data?.onJob)},${bool(data?.specialJob)},${bool(data?.refuelActive)}
        )
        ON CONFLICT (user_id) DO UPDATE SET
          device_id=EXCLUDED.device_id,recorded_at=EXCLUDED.recorded_at,connected=EXCLUDED.connected,game=EXCLUDED.game,game_paused=EXCLUDED.game_paused,
          engine_enabled=EXCLUDED.engine_enabled,electric_enabled=EXCLUDED.electric_enabled,speed_kph=EXCLUDED.speed_kph,speed_limit_kph=EXCLUDED.speed_limit_kph,
          rpm=EXCLUDED.rpm,gear=EXCLUDED.gear,fuel_l=EXCLUDED.fuel_l,fuel_range_km=EXCLUDED.fuel_range_km,fuel_avg_consumption=EXCLUDED.fuel_avg_consumption,
          adblue_l=EXCLUDED.adblue_l,oil_pressure=EXCLUDED.oil_pressure,oil_temperature=EXCLUDED.oil_temperature,water_temperature=EXCLUDED.water_temperature,
          battery_voltage=EXCLUDED.battery_voltage,odometer_km=EXCLUDED.odometer_km,world_x=EXCLUDED.world_x,world_y=EXCLUDED.world_y,world_z=EXCLUDED.world_z,heading_deg=EXCLUDED.heading_deg,pitch_deg=EXCLUDED.pitch_deg,roll_deg=EXCLUDED.roll_deg,position_valid=EXCLUDED.position_valid,air_pressure=EXCLUDED.air_pressure,brake_temperature=EXCLUDED.brake_temperature,
          parking_brake=EXCLUDED.parking_brake,motor_brake=EXCLUDED.motor_brake,brake_light=EXCLUDED.brake_light,cruise_control=EXCLUDED.cruise_control,cruise_speed_kph=EXCLUDED.cruise_speed_kph,
          retarder_level=EXCLUDED.retarder_level,user_throttle=EXCLUDED.user_throttle,effective_throttle=EXCLUDED.effective_throttle,user_brake=EXCLUDED.user_brake,effective_brake=EXCLUDED.effective_brake,
          wear_engine=EXCLUDED.wear_engine,wear_transmission=EXCLUDED.wear_transmission,wear_cabin=EXCLUDED.wear_cabin,wear_chassis=EXCLUDED.wear_chassis,wear_wheels=EXCLUDED.wear_wheels,
          cargo_damage=EXCLUDED.cargo_damage,air_pressure_warning=EXCLUDED.air_pressure_warning,air_pressure_emergency=EXCLUDED.air_pressure_emergency,fuel_warning=EXCLUDED.fuel_warning,
          adblue_warning=EXCLUDED.adblue_warning,oil_pressure_warning=EXCLUDED.oil_pressure_warning,water_temperature_warning=EXCLUDED.water_temperature_warning,battery_voltage_warning=EXCLUDED.battery_voltage_warning,
          wipers=EXCLUDED.wipers,blinker_left_active=EXCLUDED.blinker_left_active,blinker_right_active=EXCLUDED.blinker_right_active,lights_parking=EXCLUDED.lights_parking,lights_brake=EXCLUDED.lights_brake,
          lights_reverse=EXCLUDED.lights_reverse,lights_hazard=EXCLUDED.lights_hazard,differential_lock=EXCLUDED.differential_lock,lift_axle=EXCLUDED.lift_axle,trailer_lift_axle=EXCLUDED.trailer_lift_axle,
          truck_brand=EXCLUDED.truck_brand,truck_model=EXCLUDED.truck_model,license_plate=EXCLUDED.license_plate,cargo=EXCLUDED.cargo,cargo_mass_kg=EXCLUDED.cargo_mass_kg,
          source_city=EXCLUDED.source_city,destination_city=EXCLUDED.destination_city,source_company=EXCLUDED.source_company,destination_company=EXCLUDED.destination_company,
          planned_distance_km=EXCLUDED.planned_distance_km,cargo_value_brl=EXCLUDED.cargo_value_brl,on_job=EXCLUDED.on_job,special_job=EXCLUDED.special_job,refuel_active=EXCLUDED.refuel_active
      `

      return c.json({ ok: true, recordedAt: recordedAt.toISOString() }, { headers: { 'Cache-Control': 'no-store' } })
    } catch (error) {
      console.error('device_telemetry_post_error', error)
      return c.json({ ok: false, error: 'Erro ao registrar a telemetria ao vivo.' }, 500)
    }
  })
}
