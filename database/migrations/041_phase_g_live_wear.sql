-- TransPoli Etapa 7 — sincroniza desgaste/odômetro da telemetria ao caminhão da garagem.
CREATE OR REPLACE FUNCTION truckhub_sync_garage_live_telemetry()
RETURNS TRIGGER AS $$
DECLARE v_truck_id UUID;
BEGIN
  SELECT t.id INTO v_truck_id
    FROM trucks t
    JOIN garage_assignments g ON g.truck_id=t.id AND g.user_id=NEW.user_id AND g.active=TRUE
   WHERE LOWER(COALESCE(t.brand,''))=LOWER(COALESCE(NEW.truck_brand,''))
     AND LOWER(COALESCE(t.model,''))=LOWER(COALESCE(NEW.truck_model,''))
     AND LOWER(COALESCE(t.license_plate,''))=LOWER(COALESCE(NEW.license_plate,''))
   LIMIT 1;
  IF v_truck_id IS NULL THEN RETURN NEW; END IF;
  UPDATE trucks SET
    current_odometer_km=NEW.odometer_km,
    current_fuel_l=NEW.fuel_l,
    wear_pct=GREATEST(NEW.wear_engine,NEW.wear_transmission,NEW.wear_cabin,NEW.wear_chassis,NEW.wear_wheels),
    last_telemetry_at=NEW.recorded_at,
    operational_state=CASE WHEN NEW.game_paused THEN 'paused' ELSE 'normal' END,
    updated_at=NOW()
  WHERE id=v_truck_id;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_truckhub_sync_garage_live_telemetry ON device_telemetry_latest;
CREATE TRIGGER trg_truckhub_sync_garage_live_telemetry
AFTER INSERT OR UPDATE ON device_telemetry_latest
FOR EACH ROW EXECUTE FUNCTION truckhub_sync_garage_live_telemetry();
