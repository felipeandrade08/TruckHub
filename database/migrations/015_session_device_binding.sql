-- TruckHub V1 — vínculo de sessão ao dispositivo.
-- Permite invalidar sessões antigas do desktop sem derrubar a sessão web do usuário.

ALTER TABLE sessions
  ADD COLUMN IF NOT EXISTS device_id UUID REFERENCES devices(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_sessions_device
  ON sessions(device_id, revoked_at, expires_at);

-- Sessões antigas sem vínculo continuam válidas até expirar/revogar.
-- Novas ativações passam a criar sessões vinculadas ao dispositivo.
