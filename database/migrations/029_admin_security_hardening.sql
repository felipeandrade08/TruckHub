-- TruckHub Admin Center — hardening de login e sessões

ALTER TABLE admin_users
  ADD COLUMN IF NOT EXISTS failed_login_count INTEGER NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS locked_until TIMESTAMPTZ;

ALTER TABLE admin_sessions
  ADD COLUMN IF NOT EXISTS csrf_token_hash TEXT;

CREATE INDEX IF NOT EXISTS idx_admin_users_lock
  ON admin_users(email, locked_until);

CREATE INDEX IF NOT EXISTS idx_admin_sessions_expiry
  ON admin_sessions(expires_at)
  WHERE revoked_at IS NULL;

ALTER TABLE admin_users
  ADD CONSTRAINT admin_users_failed_login_nonnegative
  CHECK (failed_login_count >= 0) NOT VALID;
