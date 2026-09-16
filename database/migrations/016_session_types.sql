-- TruckHub V1 — separação explícita entre sessões web e desktop.
-- A ativação do desktop nunca deve revogar a sessão web do usuário.

ALTER TABLE sessions
  ADD COLUMN IF NOT EXISTS session_type VARCHAR(16) NOT NULL DEFAULT 'web';

ALTER TABLE sessions
  DROP CONSTRAINT IF EXISTS sessions_session_type_valid;

ALTER TABLE sessions
  ADD CONSTRAINT sessions_session_type_valid
  CHECK (session_type IN ('web', 'desktop')) NOT VALID;

CREATE INDEX IF NOT EXISTS idx_sessions_user_type_status
  ON sessions(user_id, session_type, revoked_at, expires_at);

CREATE INDEX IF NOT EXISTS idx_sessions_desktop_device
  ON sessions(user_id, device_id, session_type, revoked_at, expires_at);
