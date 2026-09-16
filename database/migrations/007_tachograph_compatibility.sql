-- TruckHub V1 — compatibilidade do resumo do tacógrafo.
-- Normaliza a tabela para o contrato usado pela API atual.

ALTER TABLE tachographs
  ADD COLUMN IF NOT EXISTS paused_seconds INTEGER NOT NULL DEFAULT 0;

ALTER TABLE tachographs
  ADD COLUMN IF NOT EXISTS total_seconds INTEGER NOT NULL DEFAULT 0;

ALTER TABLE tachographs
  ADD COLUMN IF NOT EXISTS fuel_used_l NUMERIC(12,2) NOT NULL DEFAULT 0;

ALTER TABLE tachographs
  ADD COLUMN IF NOT EXISTS average_speed_kph NUMERIC(8,2) NOT NULL DEFAULT 0;

ALTER TABLE tachographs
  ADD COLUMN IF NOT EXISTS max_speed_kph NUMERIC(8,2) NOT NULL DEFAULT 0;

ALTER TABLE tachographs
  DROP COLUMN IF EXISTS resting_seconds;

ALTER TABLE tachographs
  DROP COLUMN IF EXISTS average_speed;

ALTER TABLE tachographs
  DROP COLUMN IF EXISTS max_speed;

ALTER TABLE tachographs
  ADD CONSTRAINT tachographs_driving_seconds_nonnegative
  CHECK (driving_seconds >= 0) NOT VALID;

ALTER TABLE tachographs
  ADD CONSTRAINT tachographs_stopped_seconds_nonnegative
  CHECK (stopped_seconds >= 0) NOT VALID;

ALTER TABLE tachographs
  ADD CONSTRAINT tachographs_paused_seconds_nonnegative
  CHECK (paused_seconds >= 0) NOT VALID;

ALTER TABLE tachographs
  ADD CONSTRAINT tachographs_total_seconds_nonnegative
  CHECK (total_seconds >= 0) NOT VALID;

ALTER TABLE tachographs
  ADD CONSTRAINT tachographs_distance_km_nonnegative
  CHECK (distance_km >= 0) NOT VALID;

ALTER TABLE tachographs
  ADD CONSTRAINT tachographs_fuel_used_l_nonnegative
  CHECK (fuel_used_l >= 0) NOT VALID;

ALTER TABLE tachographs
  ADD CONSTRAINT tachographs_average_speed_kph_nonnegative
  CHECK (average_speed_kph >= 0) NOT VALID;

ALTER TABLE tachographs
  ADD CONSTRAINT tachographs_max_speed_kph_nonnegative
  CHECK (max_speed_kph >= 0) NOT VALID;
