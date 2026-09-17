-- TruckHub V1 — proteção contra criação excessiva/concorrente de checkouts.
-- Migration consolidada: incorpora o antigo 013_payment_checkout_hardening.sql.

CREATE INDEX IF NOT EXISTS idx_security_rate_limits_updated_scope
  ON security_rate_limits(updated_at, key_hash);

CREATE UNIQUE INDEX IF NOT EXISTS uq_payments_one_pending_per_user
  ON payments(user_id, provider)
  WHERE provider = 'mercado_pago' AND status = 'pending';

CREATE INDEX IF NOT EXISTS idx_payments_user_recent
  ON payments(user_id, provider, created_at DESC);
