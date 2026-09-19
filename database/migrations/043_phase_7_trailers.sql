-- TransPoli Etapa 7.2 — inventário de reboques do motorista.
CREATE TABLE IF NOT EXISTS garage_trailers (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  trailer_key VARCHAR(220) NOT NULL,
  trailer_name VARCHAR(160) NOT NULL,
  brand VARCHAR(80),
  model VARCHAR(120),
  license_plate VARCHAR(32),
  profile_name VARCHAR(160),
  owned_from_save BOOLEAN NOT NULL DEFAULT TRUE,
  active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_garage_trailer_user_key
  ON garage_trailers(user_id, trailer_key);
CREATE INDEX IF NOT EXISTS idx_garage_trailer_user_active
  ON garage_trailers(user_id, active, created_at DESC);
