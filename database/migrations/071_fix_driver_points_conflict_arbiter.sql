-- TransPoli — corrige o arbiter parcial usado pelo trigger de pontos.
-- uq_driver_points_trip é UNIQUE(trip_id) WHERE trip_id IS NOT NULL, portanto
-- o ON CONFLICT precisa repetir o predicado para o PostgreSQL inferir o índice.
CREATE OR REPLACE FUNCTION award_trip_points()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
  d NUMERIC := GREATEST(0, COALESCE((NEW.metadata->>'distanceKm')::NUMERIC, 0));
  rate NUMERIC := GREATEST(0, COALESCE((NEW.metadata->>'rateBrlKm')::NUMERIC, 4));
  damage NUMERIC := LEAST(1, GREATEST(0, COALESCE((NEW.metadata->>'cargoDamage')::NUMERIC, 0)));
  consumption NUMERIC := GREATEST(0, COALESCE((NEW.metadata->>'consumptionLKm')::NUMERIC, 0));
  clean BOOLEAN := COALESCE((NEW.metadata->>'cleanDelivery')::BOOLEAN, TRUE);
  pts INTEGER := 0;
BEGIN
  IF NEW.entry_type <> 'trip_income' OR NEW.trip_id IS NULL THEN
    RETURN NEW;
  END IF;

  pts := pts + FLOOR(d / 10)::INTEGER;
  pts := pts + LEAST(150, GREATEST(0, FLOOR((rate - 4) * 50)::INTEGER));

  IF consumption > 0 AND consumption <= 0.45 AND d >= 10 THEN
    pts := pts + 100;
  END IF;

  IF clean AND damage <= 0.01 THEN
    pts := pts + 100;
  ELSE
    pts := pts - LEAST(100, FLOOR(damage * 100)::INTEGER);
  END IF;

  IF d >= 500 THEN pts := pts + 50; END IF;
  IF d >= 1000 THEN pts := pts + 100; END IF;

  pts := GREATEST(0, pts);

  INSERT INTO driver_points_accounts(user_id, points)
  VALUES(NEW.user_id, pts)
  ON CONFLICT(user_id) DO UPDATE
     SET points = driver_points_accounts.points + EXCLUDED.points,
         updated_at = NOW();

  INSERT INTO driver_points_ledger(user_id, trip_id, points, description, metadata)
  VALUES(
    NEW.user_id,
    NEW.trip_id,
    pts,
    'Pontos da viagem concluída',
    jsonb_build_object(
      'distanceKm', d,
      'rateBrlKm', rate,
      'cargoDamage', damage,
      'consumptionLKm', consumption,
      'cleanDelivery', clean
    )
  )
  ON CONFLICT(trip_id) WHERE trip_id IS NOT NULL DO NOTHING;

  RETURN NEW;
END;
$$;

