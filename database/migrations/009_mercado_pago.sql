-- TruckHub V1 — preparação do ciclo de pagamentos Mercado Pago.
-- Não contém credenciais nem ativa licença por redirect.

ALTER TABLE payments
  ADD COLUMN IF NOT EXISTS preference_id VARCHAR(180),
  ADD COLUMN IF NOT EXISTS external_reference VARCHAR(180),
  ADD COLUMN IF NOT EXISTS provider_status VARCHAR(40),
  ADD COLUMN IF NOT EXISTS status_detail VARCHAR(120),
  ADD COLUMN IF NOT EXISTS webhook_event_id VARCHAR(180),
  ADD COLUMN IF NOT EXISTS processed_at TIMESTAMPTZ;

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_provider_event
  ON payments(provider, webhook_event_id)
  WHERE webhook_event_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_external_reference
  ON payments(provider, external_reference)
  WHERE external_reference IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_payments_preference
  ON payments(provider, preference_id);

CREATE INDEX IF NOT EXISTS idx_payments_provider_status
  ON payments(provider, provider_status);
