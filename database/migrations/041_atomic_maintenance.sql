-- TransPoli v1.0.32 — manutenção idempotente e contabilidade atômica.
-- Um source_key representa uma única operação confirmada pelo desktop/outbox.

CREATE OR REPLACE FUNCTION apply_maintenance_service(
  p_user_id UUID,
  p_truck_id UUID,
  p_trip_id UUID,
  p_source_key VARCHAR,
  p_service_type VARCHAR,
  p_component VARCHAR,
  p_description VARCHAR,
  p_cost_brl NUMERIC,
  p_odometer_km NUMERIC,
  p_wear_engine NUMERIC,
  p_wear_transmission NUMERIC,
  p_wear_cabin NUMERIC,
  p_wear_chassis NUMERIC,
  p_wear_wheels NUMERIC
)
RETURNS TABLE(record_id UUID, duplicate BOOLEAN)
LANGUAGE plpgsql
AS $$
DECLARE
  v_record_id UUID;
BEGIN
  IF p_source_key IS NULL OR BTRIM(p_source_key) = '' THEN
    RAISE EXCEPTION 'maintenance source_key is required';
  END IF;

  SELECT id INTO v_record_id
    FROM truck_maintenance_records
   WHERE user_id = p_user_id
     AND source_key = p_source_key
   LIMIT 1;

  IF v_record_id IS NOT NULL THEN
    RETURN QUERY SELECT v_record_id, TRUE;
    RETURN;
  END IF;

  INSERT INTO truck_maintenance_records(
    user_id,truck_id,service_type,component,description,cost_brl,odometer_km,
    wear_engine,wear_transmission,wear_cabin,wear_chassis,wear_wheels,source_key
  )
  VALUES(
    p_user_id,p_truck_id,p_service_type,p_component,p_description,p_cost_brl,p_odometer_km,
    p_wear_engine,p_wear_transmission,p_wear_cabin,p_wear_chassis,p_wear_wheels,p_source_key
  )
  RETURNING id INTO v_record_id;

  IF COALESCE(p_cost_brl,0) > 0 THEN
    INSERT INTO expenses(user_id,trip_id,type,description,amount)
    VALUES(
      p_user_id,
      p_trip_id,
      'maintenance',
      'Manutenção: ' || COALESCE(NULLIF(BTRIM(p_service_type),''),'Manutenção') ||
        ' • ' || COALESCE(NULLIF(BTRIM(p_component),''),'Geral'),
      p_cost_brl
    );
  END IF;

  RETURN QUERY SELECT v_record_id, FALSE;
END;
$$;
