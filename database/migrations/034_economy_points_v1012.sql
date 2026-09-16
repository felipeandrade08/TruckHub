-- TruckHub V1.0.12 — Pontos de desempenho + piso de R$ 4/km.
-- Os pontos são calculados automaticamente quando uma viagem é liquidada.
-- Migration idempotente; o runner registra este arquivo em truckhub_schema_migrations.

-- ============================================================
-- 1. PISO DE REMUNERAÇÃO
-- ============================================================
UPDATE cargo_rates
   SET rate_brl_km = 4.00,
       updated_at = NOW()
 WHERE rate_brl_km < 4.00;

-- ============================================================
-- 2. CONTAS DE PONTOS
-- ============================================================
CREATE TABLE IF NOT EXISTS driver_points_accounts (
  user_id UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
  points BIGINT NOT NULL DEFAULT 0 CHECK (points >= 0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS driver_points_ledger (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  points INTEGER NOT NULL,
  description VARCHAR(255) NOT NULL,
  metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_driver_points_ledger_user_created
  ON driver_points_ledger(user_id, created_at DESC);
CREATE UNIQUE INDEX IF NOT EXISTS uq_driver_points_trip
  ON driver_points_ledger(trip_id)
 WHERE trip_id IS NOT NULL;

-- ============================================================
-- 3. CÁLCULO AUTOMÁTICO DOS PONTOS
-- ============================================================
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
  ON CONFLICT(trip_id) DO NOTHING;

  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_award_trip_points ON economy_ledger;
CREATE TRIGGER trg_award_trip_points
AFTER INSERT ON economy_ledger
FOR EACH ROW EXECUTE FUNCTION award_trip_points();
