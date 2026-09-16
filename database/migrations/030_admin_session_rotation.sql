-- TruckHub Admin Center — rotação controlada e expiração absoluta de sessões

ALTER TABLE admin_sessions
  ADD COLUMN IF NOT EXISTS absolute_expires_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS rotated_at TIMESTAMPTZ;

UPDATE admin_sessions
SET absolute_expires_at = expires_at
WHERE absolute_expires_at IS NULL;

ALTER TABLE admin_sessions
  ADD CONSTRAINT admin_sessions_absolute_expiry_valid
  CHECK (absolute_expires_at IS NULL OR absolute_expires_at >= created_at) NOT VALID;

CREATE INDEX IF NOT EXISTS idx_admin_sessions_admin_active
  ON admin_sessions(admin_user_id, expires_at)
  WHERE revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_admin_sessions_cleanup
  ON admin_sessions(expires_at, revoked_at);
