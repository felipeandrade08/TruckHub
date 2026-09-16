-- TruckHub V1 — geração automática dos 3 documentos internos ao finalizar uma viagem.
-- Documentos são do simulador e não possuem validade fiscal/legal.

CREATE UNIQUE INDEX IF NOT EXISTS uq_documents_trip_type
  ON documents(trip_id, document_type)
  WHERE trip_id IS NOT NULL;

CREATE OR REPLACE FUNCTION truckhub_generate_trip_documents()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
  truck RECORD;
  trip_value NUMERIC(12,2);
BEGIN
  IF NEW.status = 'finished' AND (OLD.status IS DISTINCT FROM 'finished') THEN
    SELECT brand, model, license_plate
      INTO truck
      FROM trucks
     WHERE id = NEW.truck_id
     LIMIT 1;

    trip_value := NEW.cargo_value_brl;

    INSERT INTO documents (user_id, trip_id, document_type, title, document_data)
    VALUES
      (
        NEW.user_id,
        NEW.id,
        'loading-order',
        'Ordem de Carregamento',
        jsonb_build_object(
          'generatedAutomatically', true,
          'simulatorDocument', true,
          'tripId', NEW.id,
          'dateTime', NEW.started_at,
          'truckBrand', COALESCE(NEW.truck_brand, truck.brand),
          'truckModel', COALESCE(NEW.truck_model, truck.model),
          'licensePlate', COALESCE(NEW.license_plate, truck.license_plate),
          'cargo', NEW.cargo,
          'cargoMassKg', NEW.cargo_mass_kg,
          'cargoValueBrl', trip_value,
          'origin', NEW.origin,
          'destination', NEW.destination,
          'sourceCompany', NEW.source_company,
          'destinationCompany', NEW.destination_company,
          'plannedDistanceKm', NEW.planned_distance_km
        )
      ),
      (
        NEW.user_id,
        NEW.id,
        'delivery-proof',
        'Comprovante de Entrega',
        jsonb_build_object(
          'generatedAutomatically', true,
          'simulatorDocument', true,
          'tripId', NEW.id,
          'dateTime', NEW.finished_at,
          'truckBrand', COALESCE(NEW.truck_brand, truck.brand),
          'truckModel', COALESCE(NEW.truck_model, truck.model),
          'licensePlate', COALESCE(NEW.license_plate, truck.license_plate),
          'cargo', NEW.cargo,
          'cargoMassKg', NEW.cargo_mass_kg,
          'cargoValueBrl', trip_value,
          'origin', NEW.origin,
          'destination', NEW.destination,
          'destinationCompany', NEW.destination_company,
          'distanceKm', NEW.distance_km,
          'fuelUsedL', NEW.fuel_used_l,
          'status', 'Entregue'
        )
      ),
      (
        NEW.user_id,
        NEW.id,
        'cargo-note',
        'Documento da Carga',
        jsonb_build_object(
          'generatedAutomatically', true,
          'simulatorDocument', true,
          'tripId', NEW.id,
          'emissionDateTime', NEW.started_at,
          'truckBrand', COALESCE(NEW.truck_brand, truck.brand),
          'truckModel', COALESCE(NEW.truck_model, truck.model),
          'licensePlate', COALESCE(NEW.license_plate, truck.license_plate),
          'cargo', NEW.cargo,
          'cargoMassKg', NEW.cargo_mass_kg,
          'cargoValueBrl', trip_value,
          'origin', NEW.origin,
          'destination', NEW.destination,
          'sourceCompany', NEW.source_company,
          'destinationCompany', NEW.destination_company
        )
      )
    ON CONFLICT (trip_id, document_type) WHERE trip_id IS NOT NULL DO NOTHING;
  END IF;

  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_trips_generate_documents ON trips;

CREATE TRIGGER trg_trips_generate_documents
AFTER UPDATE OF status ON trips
FOR EACH ROW
EXECUTE FUNCTION truckhub_generate_trip_documents();
