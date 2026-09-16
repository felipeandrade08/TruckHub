-- TruckHub V1 — telemetria e fechamento automático de viagens
-- Executar depois de database/schema.sql.
-- Não armazena cada leitura de 100 ms: o desktop pode registrar eventos/waypoints
-- em intervalos maiores para manter o banco leve.

ALTER TABLE trips
  ADD COLUMN IF NOT EXISTS start_odometer_km NUMERIC(12,2),
  ADD COLUMN IF NOT EXISTS end_odometer_km NUMERIC(12,2),
  ADD COLUMN IF NOT EXISTS start_fuel_l NUMERIC(12,2),
  ADD COLUMN IF NOT EXISTS end_fuel_l NUMERIC(12,2),
  ADD COLUMN IF NOT EXISTS planned_distance_km NUMERIC(12,2),
  ADD COLUMN IF NOT EXISTS truck_brand VARCHAR(80),
  ADD COLUMN IF NOT EXISTS truck_model VARCHAR(120),
  ADD COLUMN IF NOT EXISTS license_plate VARCHAR(32),
  ADD COLUMN IF NOT EXISTS source_company VARCHAR(180),
  ADD COLUMN IF NOT EXISTS destination_company VARCHAR(180),
  ADD COLUMN IF NOT EXISTS cargo_mass_kg NUMERIC(12,2);

CREATE TABLE IF NOT EXISTS trip_telemetry_samples (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  trip_id UUID NOT NULL REFERENCES trips(id) ON DELETE CASCADE,
  recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  speed_kph NUMERIC(8,2),
  rpm NUMERIC(8,2),
  gear INTEGER,
  fuel_l NUMERIC(12,2),
  odometer_km NUMERIC(12,2),
  fuel_range_km NUMERIC(12,2),
  game_paused BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX IF NOT EXISTS idx_trip_telemetry_trip_time
  ON trip_telemetry_samples(trip_id, recorded_at);

-- Proteção contra leituras excessivamente frequentes por acidente.
-- O desktop deve preferir registrar uma amostra a cada 5–10 segundos.
