-- TruckHub V1 — endurecimento dos dados de telemetria.
-- Regras preventivas para impedir amostras fisicamente impossíveis ou valores absurdos.

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_speed_nonnegative
  CHECK (speed_kph >= 0) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_rpm_nonnegative
  CHECK (rpm >= 0) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_fuel_nonnegative
  CHECK (fuel_l >= 0) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_odometer_nonnegative
  CHECK (odometer_km >= 0) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_range_nonnegative
  CHECK (fuel_range_km >= 0) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_speed_reasonable
  CHECK (speed_kph <= 250) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_rpm_reasonable
  CHECK (rpm <= 10000) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_gear_reasonable
  CHECK (gear BETWEEN -10 AND 20) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_fuel_reasonable
  CHECK (fuel_l <= 2000) NOT VALID;

ALTER TABLE trip_telemetry_samples
  ADD CONSTRAINT trip_telemetry_range_reasonable
  CHECK (fuel_range_km <= 10000) NOT VALID;

ALTER TABLE trips
  ADD CONSTRAINT trips_cargo_value_nonnegative
  CHECK (cargo_value_brl IS NULL OR cargo_value_brl >= 0) NOT VALID;

ALTER TABLE trips
  ADD CONSTRAINT trips_cargo_mass_nonnegative
  CHECK (cargo_mass_kg IS NULL OR cargo_mass_kg >= 0) NOT VALID;

ALTER TABLE trips
  ADD CONSTRAINT trips_planned_distance_nonnegative
  CHECK (planned_distance_km IS NULL OR planned_distance_km >= 0) NOT VALID;

CREATE INDEX IF NOT EXISTS idx_trip_telemetry_trip_recorded
  ON trip_telemetry_samples(trip_id, recorded_at);
