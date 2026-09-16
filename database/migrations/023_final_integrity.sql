-- TruckHub V1 — proteção final de invariantes.
-- Migration consolidada: incorpora a antiga 023_payment_integrity.sql.
-- NOT VALID preserva dados históricos; as regras passam a valer para novas alterações.

ALTER TABLE sessions
  DROP CONSTRAINT IF EXISTS sessions_expiry_after_creation,
  ADD CONSTRAINT sessions_expiry_after_creation
    CHECK (expires_at > created_at) NOT VALID;

ALTER TABLE sessions
  DROP CONSTRAINT IF EXISTS sessions_revoked_after_creation,
  ADD CONSTRAINT sessions_revoked_after_creation
    CHECK (revoked_at IS NULL OR revoked_at >= created_at) NOT VALID;

ALTER TABLE devices
  DROP CONSTRAINT IF EXISTS devices_last_seen_after_first_seen,
  ADD CONSTRAINT devices_last_seen_after_first_seen
    CHECK (last_seen_at >= first_seen_at) NOT VALID;

ALTER TABLE payments
  DROP CONSTRAINT IF EXISTS payments_provider_valid,
  ADD CONSTRAINT payments_provider_valid
    CHECK (provider IN ('mercado_pago')) NOT VALID;

ALTER TABLE payments
  DROP CONSTRAINT IF EXISTS payments_currency_brl,
  DROP CONSTRAINT IF EXISTS payments_currency_valid,
  ADD CONSTRAINT payments_currency_brl
    CHECK (currency = 'BRL') NOT VALID;

ALTER TABLE payments
  DROP CONSTRAINT IF EXISTS payments_status_valid,
  ADD CONSTRAINT payments_status_valid
    CHECK (status IN ('pending','approved','rejected','cancelled','refunded','charged_back','review','failed','in_process','unknown')) NOT VALID;

ALTER TABLE payments
  DROP CONSTRAINT IF EXISTS payments_amount_reasonable,
  ADD CONSTRAINT payments_amount_reasonable
    CHECK (amount > 0 AND amount <= 100000) NOT VALID;

ALTER TABLE payments
  DROP CONSTRAINT IF EXISTS payments_paid_at_valid,
  ADD CONSTRAINT payments_paid_at_valid
    CHECK (paid_at IS NULL OR paid_at >= created_at) NOT VALID;

ALTER TABLE trip_events
  DROP CONSTRAINT IF EXISTS trip_events_payload_object,
  ADD CONSTRAINT trip_events_payload_object
    CHECK (jsonb_typeof(payload) = 'object') NOT VALID;

CREATE INDEX IF NOT EXISTS idx_sessions_active_expiry
  ON sessions(expires_at)
  WHERE revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_devices_active_last_seen
  ON devices(last_seen_at DESC)
  WHERE status = 'active';

CREATE INDEX IF NOT EXISTS idx_payments_provider_created
  ON payments(provider, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_payments_webhook_event
  ON payments(webhook_event_id)
  WHERE webhook_event_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_mp_webhook_events_payment_result
  ON mercado_pago_webhook_events(payment_id, result, received_at DESC);
