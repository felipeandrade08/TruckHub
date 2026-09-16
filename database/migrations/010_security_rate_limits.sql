-- TruckHub V1 — rate limiting persistente para endpoints sensíveis.
-- A chave armazenada é um hash; não persistimos e-mail ou IP em claro.

CREATE TABLE IF NOT EXISTS security_rate_limits (
  key_hash TEXT PRIMARY KEY,
  window_started_at TIMESTAMPTZ NOT NULL,
  attempts INTEGER NOT NULL DEFAULT 0,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT security_rate_limits_attempts_nonnegative CHECK (attempts >= 0)
);

CREATE INDEX IF NOT EXISTS idx_security_rate_limits_updated
  ON security_rate_limits(updated_at);
