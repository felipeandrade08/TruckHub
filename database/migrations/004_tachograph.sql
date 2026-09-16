-- TruckHub V1 — resumo inteligente do tacógrafo por viagem
-- A telemetria bruta continua em trip_telemetry_samples.
-- Esta tabela guarda somente o resumo calculado, mantendo o banco leve.

CREATE TABLE IF NOT EXISTS tachographs (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  trip_id UUID NOT NULL UNIQUE REFERENCES trips(id) ON DELETE CASCADE,
  driving_seconds INTEGER NOT NULL DEFAULT 0 CHECK (driving_seconds >= 0),
  stopped_seconds INTEGER NOT NULL DEFAULT 0 CHECK (stopped_seconds >= 0),
  paused_seconds INTEGER NOT NULL DEFAULT 0 CHECK (paused_seconds >= 0),
  total_seconds INTEGER NOT NULL DEFAULT 0 CHECK (total_seconds >= 0),
  distance_km NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (distance_km >= 0),
  fuel_used_l NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (fuel_used_l >= 0),
  average_speed_kph NUMERIC(8,2) NOT NULL DEFAULT 0 CHECK (average_speed_kph >= 0),
  max_speed_kph NUMERIC(8,2) NOT NULL DEFAULT 0 CHECK (max_speed_kph >= 0),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_tachographs_trip ON tachographs(trip_id);
