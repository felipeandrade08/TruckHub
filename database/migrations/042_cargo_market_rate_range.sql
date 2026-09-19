-- TransPoli — Mercado de Cargas: permitir tarifas reais de R$ 5,00 a R$ 12,00/km.
-- Corrige o limite legado de R$ 6,00 que fazia cargas novas falharem ao serem cadastradas.

DO $$
DECLARE
  c RECORD;
BEGIN
  FOR c IN
    SELECT conrelid::regclass AS table_name, conname
    FROM pg_constraint
    WHERE conrelid IN ('cargo_market_offers'::regclass, 'cargo_contracts'::regclass)
      AND contype = 'c'
      AND pg_get_constraintdef(oid) ILIKE '%rate_brl_km%'
      AND pg_get_constraintdef(oid) ILIKE '%6%'
  LOOP
    EXECUTE format('ALTER TABLE %s DROP CONSTRAINT IF EXISTS %I', c.table_name, c.conname);
  END LOOP;
END $$;

ALTER TABLE cargo_market_offers
  ADD CONSTRAINT cargo_market_offers_rate_brl_km_range
  CHECK (rate_brl_km >= 5 AND rate_brl_km <= 12);

ALTER TABLE cargo_contracts
  ADD CONSTRAINT cargo_contracts_rate_brl_km_range
  CHECK (rate_brl_km >= 5 AND rate_brl_km <= 12);

CREATE OR REPLACE FUNCTION truckhub_discover_trip_cargo()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
  k TEXT;
  display_name TEXT;
  rate NUMERIC(8,2);
  offer_id UUID;
  hash_number BIGINT;
BEGIN
  IF NEW.cargo IS NULL OR trim(NEW.cargo) = '' THEN RETURN NEW; END IF;

  k := truckhub_cargo_key(NEW.cargo);
  display_name := left(trim(NEW.cargo),180);

  hash_number := get_byte(decode(md5(k),'hex'),0)::bigint * 16777216
               + get_byte(decode(md5(k),'hex'),1)::bigint * 65536
               + get_byte(decode(md5(k),'hex'),2)::bigint * 256
               + get_byte(decode(md5(k),'hex'),3)::bigint;

  rate := round((5 + mod(hash_number,15) * 0.5)::numeric,2);

  INSERT INTO cargo_market_offers(
    cargo_key,display_name,rate_brl_km,market_status,discovered_count,last_discovered_at
  )
  VALUES(
    k,display_name,rate,
    CASE WHEN rate>=9 THEN 'high' WHEN rate<=6.5 THEN 'low' ELSE 'normal' END,
    1,NOW()
  )
  ON CONFLICT(cargo_key) DO UPDATE SET
    discovered_count=cargo_market_offers.discovered_count+1,
    last_discovered_at=NOW(),
    active=TRUE,
    updated_at=NOW();

  INSERT INTO cargo_rates(cargo_key,display_name,rate_brl_km,active)
  VALUES(k,display_name,rate,TRUE)
  ON CONFLICT(cargo_key) DO UPDATE SET
    display_name=EXCLUDED.display_name,
    rate_brl_km=EXCLUDED.rate_brl_km,
    active=TRUE,
    updated_at=NOW();

  SELECT id INTO offer_id FROM cargo_market_offers WHERE cargo_key=k LIMIT 1;

  INSERT INTO cargo_contracts(
    user_id,offer_id,cargo_key,cargo,rate_brl_km,status,origin,destination,
    distance_km,cargo_mass_kg,accepted_at,started_at,trip_id
  )
  VALUES(
    NEW.user_id,offer_id,k,display_name,rate,
    CASE WHEN NEW.status='finished' THEN 'delivered' ELSE 'active' END,
    NEW.origin,NEW.destination,NEW.distance_km,NEW.cargo_mass_kg,NOW(),
    CASE WHEN NEW.status='finished' THEN NULL ELSE NEW.started_at END,NEW.id
  );

  NEW.cargo_contract_id := (
    SELECT id FROM cargo_contracts WHERE trip_id=NEW.id ORDER BY created_at DESC LIMIT 1
  );

  RETURN NEW;
END;
$$;
