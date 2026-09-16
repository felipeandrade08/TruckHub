-- TruckHub V1 — integridade final para Neon + Mercado Pago.
-- Nenhuma credencial é armazenada no banco ou no repositório.

ALTER TABLE payments
  ADD COLUMN IF NOT EXISTS external_id VARCHAR(180),
  ADD COLUMN IF NOT EXISTS paid_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS raw_event JSONB,
  ADD COLUMN IF NOT EXISTS updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_provider_external_id
  ON payments(provider, external_id)
  WHERE external_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_payments_user_status
  ON payments(user_id, status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_payments_external_reference_status
  ON payments(external_reference, status);

CREATE INDEX IF NOT EXISTS idx_mp_webhook_events_event_id
  ON mercado_pago_webhook_events(event_id);

CREATE INDEX IF NOT EXISTS idx_mp_webhook_events_processed
  ON mercado_pago_webhook_events(processed_at, received_at);

-- Uma conta não pode possuir duas licenças simultaneamente.
CREATE UNIQUE INDEX IF NOT EXISTS uq_licenses_one_per_user
  ON licenses(user_id);

-- Viagens e despesas permanecem isoladas pelo user_id; estes índices aceleram
-- as consultas de propriedade usadas pela API.
CREATE INDEX IF NOT EXISTS idx_trips_user_status_started
  ON trips(user_id, status, started_at DESC);

CREATE INDEX IF NOT EXISTS idx_expenses_user_trip
  ON expenses(user_id, trip_id, created_at DESC);
