# TruckHub Database

Banco oficial da V1: PostgreSQL. Para desenvolvimento, use Neon PostgreSQL.

## Fonte de verdade

A fonte de verdade do banco é `database/migrations/`.

As migrations são executadas em ordem pelo `api/scripts/migrate.mjs`. O runner registra cada migration aplicada em `truckhub_schema_migrations` e executa cada migration de forma atômica junto com o registro no histórico.

`schema.sql` é apenas uma referência/bootstrap histórica da estrutura inicial. Ele não representa necessariamente o schema final e não substitui o migration runner.

## Estrutura

- `schema.sql` — referência inicial/legada do modelo.
- `migrations/000_migration_history.sql` — controle de migrations.
- `migrations/001_initial_schema.sql` — estrutura inicial.
- `migrations/002_trip_telemetry.sql` — telemetria e campos adicionais de viagem.
- `migrations/003_device_activation.sql` — ativação de dispositivo.
- `migrations/004_tachograph.sql` — tacógrafo.
- `migrations/005_trip_financials.sql` — estrutura financeira da viagem.
- `migrations/006_auto_trip_documents.sql` — documentos automáticos da viagem.
- `migrations/007_tachograph_compatibility.sql` — compatibilidade do contrato do tacógrafo.
- `migrations/008_license_hardening.sql` — endurecimento de licenças.
- `migrations/009_mercado_pago.sql` — integração e identidade de pagamentos.
- `migrations/010_security_rate_limits.sql` — rate limits.
- `migrations/011_session_hardening.sql` — endurecimento de sessões.
- `migrations/012+` — hardening, pagamentos, integridade, reconciliação e administração.
- `migrations/030_admin_session_rotation.sql` — rotação/expiração absoluta de sessões administrativas.
- `migrations/031_economy_garage.sql` — banco do motorista, tarifas, custos, empréstimos e garagem exclusiva.

## Fluxo correto

1. Criar o projeto PostgreSQL/Neon.
2. Configurar `DATABASE_URL` no ambiente da API.
3. Executar `npm run migrate` dentro de `api/`.
4. Conferir `truckhub_schema_migrations`.
5. Validar a API em `/health`.
6. Somente depois conectar o ambiente de produção da API ao banco.

## Regra importante

As migrations já aplicadas não devem ser editadas para corrigir um banco compartilhado. Crie uma nova migration numerada.

Isso mantém o histórico reproduzível e evita que instalações novas e instalações já migradas fiquem com estruturas diferentes.

## Segurança

Não coloque `DATABASE_URL`, senhas ou tokens neste repositório. O banco de produção deve ser acessado somente pelo backend; o desktop e o navegador nunca recebem `DATABASE_URL`.

## Próxima validação antes do Neon

O projeto possui migrations até `031_economy_garage.sql`. Antes de criar o banco Neon, a sequência completa `000` → `031` deve ser validada em uma instância PostgreSQL limpa, garantindo que uma instalação do zero seja reproduzível.
