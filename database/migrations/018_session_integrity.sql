-- TruckHub V1 — integridade final das sessões.
-- Sessão desktop precisa estar vinculada a um dispositivo; sessão web não precisa.

ALTER TABLE sessions
  DROP CONSTRAINT IF EXISTS sessions_device_binding_valid;

ALTER TABLE sessions
  ADD CONSTRAINT sessions_device_binding_valid
  CHECK (
    (session_type = 'desktop' AND device_id IS NOT NULL)
    OR session_type = 'web'
  ) NOT VALID;

CREATE INDEX IF NOT EXISTS idx_sessions_active_token
  ON sessions(token_hash)
  WHERE revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_sessions_active_user
  ON sessions(user_id, revoked_at, expires_at)
  WHERE revoked_at IS NULL;
