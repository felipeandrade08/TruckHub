-- TruckHub — PostgreSQL legacy/bootstrap reference
--
-- ATENÇÃO:
-- A fonte de verdade do banco NÃO é este arquivo.
-- A fonte de verdade é database/migrations/, aplicada em ordem pelo
-- api/scripts/migrate.mjs.
--
-- Este arquivo é mantido apenas como referência/bootstrap histórico da
-- estrutura inicial. Ele NÃO representa necessariamente o schema final após
-- todas as migrations e NÃO deve ser usado para substituir o migration runner.
--
-- Nunca coloque senhas, tokens ou chaves reais neste arquivo.

CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE IF NOT EXISTS users (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  name VARCHAR(120) NOT NULL,
  email VARCHAR(320) NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  pin_hash TEXT NOT NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','blocked','deleted')),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS licenses (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE RESTRICT,
  license_type VARCHAR(20) NOT NULL DEFAULT 'trial' CHECK (license_type IN ('trial','lifetime')),
  status VARCHAR(20) NOT NULL DEFAULT 'trial' CHECK (status IN ('trial','active','expired','blocked')),
  trial_started_at TIMESTAMPTZ NOT NULL,
  trial_expires_at TIMESTAMPTZ NOT NULL,
  activated_at TIMESTAMPTZ,
  expires_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CHECK (trial_expires_at > trial_started_at),
  CHECK ((license_type = 'lifetime' AND expires_at IS NULL) OR license_type = 'trial')
);

CREATE TABLE IF NOT EXISTS devices (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  license_id UUID NOT NULL REFERENCES licenses(id) ON DELETE RESTRICT,
  device_id VARCHAR(255) NOT NULL,
  device_name VARCHAR(160),
  status VARCHAR(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','revoked')),
  first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (license_id),
  UNIQUE (device_id)
);

CREATE TABLE IF NOT EXISTS sessions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  token_hash TEXT NOT NULL UNIQUE,
  expires_at TIMESTAMPTZ NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  revoked_at TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_sessions_user ON sessions(user_id);
CREATE INDEX IF NOT EXISTS idx_sessions_expires ON sessions(expires_at);

CREATE TABLE IF NOT EXISTS trucks (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  truck_name VARCHAR(120),
  brand VARCHAR(80),
  model VARCHAR(120),
  license_plate VARCHAR(32),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS trips (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  truck_id UUID REFERENCES trucks(id) ON DELETE SET NULL,
  cargo VARCHAR(180),
  origin VARCHAR(180),
  destination VARCHAR(180),
  started_at TIMESTAMPTZ NOT NULL,
  finished_at TIMESTAMPTZ,
  distance_km NUMERIC(12,2),
  fuel_used_l NUMERIC(12,2),
  status VARCHAR(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','finished','cancelled')),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CHECK (finished_at IS NULL OR finished_at >= started_at)
);

CREATE TABLE IF NOT EXISTS trip_events (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  trip_id UUID NOT NULL REFERENCES trips(id) ON DELETE CASCADE,
  event_type VARCHAR(60) NOT NULL,
  event_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  payload JSONB NOT NULL DEFAULT '{}'::jsonb
);

CREATE TABLE IF NOT EXISTS expenses (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  type VARCHAR(30) NOT NULL CHECK (type IN ('fuel','toll','maintenance','parking','food','other')),
  description VARCHAR(255),
  amount NUMERIC(12,2) NOT NULL CHECK (amount >= 0),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS documents (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  document_type VARCHAR(40) NOT NULL,
  title VARCHAR(180) NOT NULL,
  document_data JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS tachographs (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  driving_seconds INTEGER NOT NULL DEFAULT 0 CHECK (driving_seconds >= 0),
  rest_seconds INTEGER NOT NULL DEFAULT 0 CHECK (rest_seconds >= 0),
  stopped_seconds INTEGER NOT NULL DEFAULT 0 CHECK (stopped_seconds >= 0),
  distance_km NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (distance_km >= 0),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS payments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  provider VARCHAR(30) NOT NULL DEFAULT 'mercado_pago',
  external_id VARCHAR(180) UNIQUE,
  status VARCHAR(30) NOT NULL DEFAULT 'pending',
  amount NUMERIC(12,2) NOT NULL CHECK (amount >= 0),
  currency VARCHAR(3) NOT NULL DEFAULT 'BRL',
  paid_at TIMESTAMPTZ,
  raw_event JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS app_versions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  version VARCHAR(30) NOT NULL UNIQUE,
  channel VARCHAR(20) NOT NULL DEFAULT 'stable' CHECK (channel IN ('stable','beta')),
  download_url TEXT NOT NULL,
  checksum_sha256 CHAR(64),
  mandatory BOOLEAN NOT NULL DEFAULT FALSE,
  changelog TEXT,
  released_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS update_logs (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id) ON DELETE SET NULL,
  device_id UUID REFERENCES devices(id) ON DELETE SET NULL,
  from_version VARCHAR(30),
  to_version VARCHAR(30),
  status VARCHAR(20) NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_trips_user_started ON trips(user_id, started_at DESC);
CREATE INDEX IF NOT EXISTS idx_expenses_user_created ON expenses(user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_trip_events_trip_time ON trip_events(trip_id, event_at);
CREATE INDEX IF NOT EXISTS idx_payments_user_created ON payments(user_id, created_at DESC);

-- O backend deve calcular o vencimento do trial a partir do mesmo timestamp de criação:
-- trial_started_at = users.created_at
-- trial_expires_at = trial_started_at + INTERVAL '7 days'
-- Ao confirmar o pagamento: license_type='lifetime', status='active', expires_at=NULL.
