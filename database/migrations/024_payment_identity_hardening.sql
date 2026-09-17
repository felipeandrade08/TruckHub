-- TruckHub V1 — vínculo único entre pagamento local e identificadores Mercado Pago.
-- Não contém credenciais.

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_external_id
  ON payments(provider, external_id)
  WHERE external_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_preference_id
  ON payments(provider, preference_id)
  WHERE preference_id IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_webhook_event_id
  ON payments(provider, webhook_event_id)
  WHERE webhook_event_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_payments_user_external_reference
  ON payments(user_id, external_reference);
