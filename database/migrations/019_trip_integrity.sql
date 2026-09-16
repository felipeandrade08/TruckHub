-- TruckHub V1 — integridade das viagens.
-- Uma conta só pode possuir uma viagem ativa por vez e valores básicos precisam ser coerentes.

CREATE UNIQUE INDEX IF NOT EXISTS uq_trips_one_active_per_user
  ON trips(user_id)
  WHERE status = 'active';

ALTER TABLE trips
  ADD CONSTRAINT trips_distance_nonnegative
  CHECK (distance_km IS NULL OR distance_km >= 0) NOT VALID;

ALTER TABLE trips
  ADD CONSTRAINT trips_fuel_nonnegative
  CHECK (fuel_used_l IS NULL OR fuel_used_l >= 0) NOT VALID;

ALTER TABLE trips
  ADD CONSTRAINT trips_finished_status_consistent
  CHECK (
    (status = 'active' AND finished_at IS NULL)
    OR (status = 'finished' AND finished_at IS NOT NULL)
    OR status = 'cancelled'
  ) NOT VALID;

CREATE INDEX IF NOT EXISTS idx_trips_user_status
  ON trips(user_id, status, started_at DESC);
