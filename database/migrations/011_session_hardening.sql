-- TruckHub V1 — endurecimento do ciclo de vida das sessões.
-- Sessões revogadas/expiradas são mantidas para auditoria mínima e removidas após retenção.

ALTER TABLE sessions
  ADD COLUMN IF NOT EXISTS last_seen_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_sessions_cleanup
  ON sessions(expires_at, revoked_at);

CREATE INDEX IF NOT EXISTS idx_sessions_last_seen
  ON sessions(last_seen_at);

-- Limpeza inicial/operacional: remove somente sessões que já passaram do prazo
-- de retenção. Sessões válidas ou recentemente revogadas permanecem intactas.
DELETE FROM sessions
WHERE expires_at < NOW() - INTERVAL '30 days'
   OR revoked_at < NOW() - INTERVAL '30 days';
