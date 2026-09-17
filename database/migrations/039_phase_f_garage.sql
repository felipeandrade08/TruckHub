-- TruckHub V1.0.13 — FASE F: garagem real + estado operacional + histórico de uso.
-- Não altera a versão do produto e não dispara build.

ALTER TABLE trucks
  ADD COLUMN IF NOT EXISTS current_odometer_km NUMERIC(14,2) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS current_fuel_l NUMERIC(12,2),
  ADD COLUMN IF NOT EXISTS wear_pct NUMERIC(6,2) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS operational_state VARCHAR(24) NOT NULL DEFAULT 'normal',
  ADD COLUMN IF NOT EXISTS last_telemetry_at TIMESTAMPTZ;

DO $$ BEGIN
  ALTER TABLE trucks ADD CONSTRAINT trucks_wear_pct_range
    CHECK (wear_pct >= 0 AND wear_pct <= 100) NOT VALID;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

CREATE TABLE IF NOT EXISTS garage_usage_history (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  trip_id UUID NOT NULL UNIQUE REFERENCES trips(id) ON DELETE CASCADE,
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  truck_id UUID NOT NULL REFERENCES trucks(id) ON DELETE CASCADE,
  started_at TIMESTAMPTZ,
  finished_at TIMESTAMPTZ,
  start_odometer_km NUMERIC(14,2),
  end_odometer_km NUMERIC(14,2),
  distance_km NUMERIC(14,2),
  start_fuel_l NUMERIC(12,2),
  end_fuel_l NUMERIC(12,2),
  cargo VARCHAR(200),
  origin VARCHAR(150),
  destination VARCHAR(150),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_garage_usage_user_created
  ON garage_usage_history(user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_garage_usage_truck_created
  ON garage_usage_history(truck_id, created_at DESC);

-- A última leitura de telemetria passa a ser o estado atual do caminhão.
CREATE OR REPLACE FUNCTION truckhub_sync_garage_from_telemetry()
RETURNS TRIGGER AS $$
DECLARE
  v_truck_id UUID;
BEGIN
  SELECT truck_id INTO v_truck_id FROM trips WHERE id = NEW.trip_id;
  IF v_truck_id IS NULL THEN RETURN NEW; END IF;

  UPDATE trucks
     SET current_odometer_km = COALESCE(NEW.odometer_km, current_odometer_km),
         current_fuel_l = COALESCE(NEW.fuel_l, current_fuel_l),
         last_telemetry_at = COALESCE(NEW.recorded_at, NOW()),
         operational_state = CASE
           WHEN COALESCE(NEW.game_paused, FALSE) THEN 'paused'
           ELSE 'normal'
         END,
         updated_at = NOW()
   WHERE id = v_truck_id;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_truckhub_garage_telemetry ON trip_telemetry_samples;
CREATE TRIGGER trg_truckhub_garage_telemetry
AFTER INSERT ON trip_telemetry_samples
FOR EACH ROW EXECUTE FUNCTION truckhub_sync_garage_from_telemetry();

-- Cada viagem encerrada vira uma entrada de histórico da garagem.
CREATE OR REPLACE FUNCTION truckhub_register_garage_trip_history()
RETURNS TRIGGER AS $$
BEGIN
  IF NEW.status = 'finished' AND NEW.truck_id IS NOT NULL THEN
    INSERT INTO garage_usage_history(
      trip_id,user_id,truck_id,started_at,finished_at,
      start_odometer_km,end_odometer_km,distance_km,
      start_fuel_l,end_fuel_l,cargo,origin,destination
    ) VALUES (
      NEW.id,NEW.user_id,NEW.truck_id,NEW.started_at,NEW.finished_at,
      NEW.start_odometer_km,NEW.end_odometer_km,NEW.distance_km,
      NEW.start_fuel_l,NEW.end_fuel_l,NEW.cargo,NEW.origin,NEW.destination
    )
    ON CONFLICT(trip_id) DO UPDATE SET
      finished_at=EXCLUDED.finished_at,
      end_odometer_km=EXCLUDED.end_odometer_km,
      distance_km=EXCLUDED.distance_km,
      end_fuel_l=EXCLUDED.end_fuel_l,
      cargo=EXCLUDED.cargo,
      origin=EXCLUDED.origin,
      destination=EXCLUDED.destination;
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_truckhub_garage_trip_history ON trips;
CREATE TRIGGER trg_truckhub_garage_trip_history
AFTER INSERT OR UPDATE OF status ON trips
FOR EACH ROW EXECUTE FUNCTION truckhub_register_garage_trip_history();

-- Reconstrói o estado atual dos caminhões a partir da última amostra existente.
UPDATE trucks t
   SET current_odometer_km = COALESCE(last_sample.odometer_km, t.current_odometer_km),
       current_fuel_l = COALESCE(last_sample.fuel_l, t.current_fuel_l),
       last_telemetry_at = last_sample.recorded_at
  FROM (
    SELECT DISTINCT ON (tr.truck_id)
           tr.truck_id, s.odometer_km, s.fuel_l, s.recorded_at
      FROM trips tr
      JOIN trip_telemetry_samples s ON s.trip_id=tr.id
     WHERE tr.truck_id IS NOT NULL
     ORDER BY tr.truck_id, s.recorded_at DESC
  ) last_sample
 WHERE t.id=last_sample.truck_id;

-- Histórico das viagens já concluídas.
INSERT INTO garage_usage_history(
  trip_id,user_id,truck_id,started_at,finished_at,
  start_odometer_km,end_odometer_km,distance_km,
  start_fuel_l,end_fuel_l,cargo,origin,destination
)
SELECT t.id,t.user_id,t.truck_id,t.started_at,t.finished_at,
       t.start_odometer_km,t.end_odometer_km,t.distance_km,
       t.start_fuel_l,t.end_fuel_l,t.cargo,t.origin,t.destination
  FROM trips t
 WHERE t.status='finished' AND t.truck_id IS NOT NULL
ON CONFLICT(trip_id) DO NOTHING;
