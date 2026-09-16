-- TruckHub V1 — idempotência forte para webhooks Mercado Pago.
-- Cada evento recebido é registrado separadamente do pagamento.
-- Isso evita que eventos diferentes sobrescrevam a chave de idempotência anterior.

CREATE TABLE IF NOT EXISTS mercado_pago_webhook_events (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  event_id VARCHAR(180) NOT NULL UNIQUE,
  payment_id VARCHAR(180) NOT NULL,
  event_type VARCHAR(60),
  action VARCHAR(120),
  received_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  processed_at TIMESTAMPTZ,
  result VARCHAR(40),
  error_detail VARCHAR(180),
  raw_event JSONB
);

CREATE INDEX IF NOT EXISTS idx_mp_webhook_events_payment
  ON mercado_pago_webhook_events(payment_id);

CREATE INDEX IF NOT EXISTS idx_mp_webhook_events_received
  ON mercado_pago_webhook_events(received_at);

CREATE INDEX IF NOT EXISTS idx_mp_webhook_events_unprocessed
  ON mercado_pago_webhook_events(processed_at)
  WHERE processed_at IS NULL;
