-- Fase D — correção da geração determinística de tarifa para novas cargas.
-- Usa bytes do MD5 em vez de cast textual, evitando dependência do parser de bit do PostgreSQL.

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
  rate := round((4 + mod(hash_number,11) * 0.2)::numeric,2);

  INSERT INTO cargo_market_offers(cargo_key,display_name,rate_brl_km,market_status,discovered_count,last_discovered_at)
  VALUES(k,display_name,rate,CASE WHEN rate>=5.2 THEN 'high' WHEN rate<=4.4 THEN 'low' ELSE 'normal' END,1,NOW())
  ON CONFLICT(cargo_key) DO UPDATE SET discovered_count=cargo_market_offers.discovered_count+1,last_discovered_at=NOW(),active=TRUE,updated_at=NOW();

  INSERT INTO cargo_rates(cargo_key,display_name,rate_brl_km,active)
  VALUES(k,display_name,rate,TRUE)
  ON CONFLICT(cargo_key) DO UPDATE SET display_name=EXCLUDED.display_name,active=TRUE;

  SELECT id INTO offer_id FROM cargo_market_offers WHERE cargo_key=k LIMIT 1;
  INSERT INTO cargo_contracts(user_id,offer_id,cargo_key,cargo,rate_brl_km,status,origin,destination,distance_km,cargo_mass_kg,accepted_at,started_at,trip_id)
  VALUES(NEW.user_id,offer_id,k,display_name,rate,CASE WHEN NEW.status='finished' THEN 'delivered' ELSE 'active' END,NEW.origin,NEW.destination,NEW.distance_km,NEW.cargo_mass_kg,NOW(),CASE WHEN NEW.status='finished' THEN NULL ELSE NEW.started_at END,NEW.id);
  NEW.cargo_contract_id := (SELECT id FROM cargo_contracts WHERE trip_id=NEW.id ORDER BY created_at DESC LIMIT 1);
  RETURN NEW;
END;
$$;
