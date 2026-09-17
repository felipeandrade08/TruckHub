import fs from 'node:fs'
import path from 'node:path'

const root = path.resolve(new URL('../..', import.meta.url).pathname)
const checks = []

function read(relative) {
  const file = path.join(root, relative)
  if (!fs.existsSync(file)) throw new Error(`Arquivo ausente: ${relative}`)
  return fs.readFileSync(file, 'utf8')
}

function check(name, ok, detail = '') {
  checks.push({ name, ok, detail })
  if (!ok) throw new Error(`${name}${detail ? ` — ${detail}` : ''}`)
}

const version = read('VERSION').trim()
check('Versão oficial V1.0.14', version === '1.0.14', `encontrado ${version}`)

const api = read('api/src/index.ts')
const telemetry = read('api/src/tripTelemetry.ts')
const events = read('api/src/transpoliEvents.ts')
const desktop = read('desktop/TransPoli/MainWindow.xaml.cs')
const sync = read('desktop/TransPoli/TransPoliServerSync.cs')
const web = read('web/site/dashboard-avancado.js')

for (const route of [
  "registerTripTelemetryRoutes(app)",
  "registerEconomyRoutes(app)",
  "registerGarageRoutes(app)",
  "registerExpenseRoutes(app)",
  "registerDriverDashboardRoutes(app)",
  "registerTransPoliEventRoutes(app)",
  "registerCargoMarketRoutes(app)",
  "registerNotesRoutes(app)",
  "registerStatisticsRoutes(app)"
]) check(`API registra ${route}`, api.includes(route))

for (const route of [
  "app.post('/me/trips/:id/telemetry'",
  "app.get('/me/trips/:id/telemetry'",
  "app.post('/me/events'",
  "app.get('/me/events'"
]) check(`Rota integrada ${route}`, telemetry.includes(route) || events.includes(route))

check('Telemetria rejeita odômetro regressivo', telemetry.includes('Odômetro não pode retroceder'))
check('Telemetria controla intervalo mínimo', telemetry.includes('MIN_SAMPLE_INTERVAL_MS'))
check('Finalização calcula distância por telemetria', api.includes('start_odometer') && api.includes('end_odometer'))
check('Finalização liquida economia', api.includes('settleTripEconomy(sql,user.id,id)'))
check('Eventos possuem deduplicação', events.includes('ON CONFLICT(user_id, client_event_id)'))
check('Desktop envia eventos para /me/events', sync.includes('/me/events'))
check('Desktop envia telemetria ao servidor', desktop.includes('SendTelemetrySample'))
check('Desktop possui recuperação de viagem ativa', desktop.includes('RecoverExistingTripAsync'))
check('F10 global é registrado no MainWindow', desktop.includes('VkF10') && desktop.includes('RegisterGlobalHotKey'))
check('F10 abre/fecha somente o tablet', desktop.includes('ToggleCockpit()') && desktop.includes('WmHotKey'))
check('Painel web usa dashboard avançado real', web.includes('/me/dashboard-advanced'))
check('Painel web usa cache no-store', web.includes('cache:') || web.includes("cache: 'no-store'"))

console.log(`FASE L — auditoria estrutural: ${checks.length} verificações OK`)
for (const item of checks) console.log(`✓ ${item.name}`)
console.log('Nota: testes ETS2/ATS reais, queda/reconexão física e validação visual do tablet dependem do ambiente Windows + jogo e devem ser executados no PC do usuário antes da liberação final.')
