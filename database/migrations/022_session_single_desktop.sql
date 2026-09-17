-- TruckHub V1 — integridade adicional das sessões desktop.
-- Uma conta pode manter sessões web, mas somente uma sessão desktop ativa por vez.
-- O vínculo do desktop com o dispositivo continua sendo obrigatório pela migration 018.

CREATE UNIQUE INDEX IF NOT EXISTS uq_sessions_one_active_desktop_per_user
  ON sessions(user_id)
  WHERE session_type = 'desktop' AND revoked_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_sessions_device_active
  ON sessions(device_id, revoked_at, expires_at)
  WHERE device_id IS NOT NULL AND revoked_at IS NULL;
