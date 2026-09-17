-- Fase D — descoberta automática de cargas reais.
-- Sempre que o ETS2/ATS fizer o TruckHub criar uma viagem com uma carga,
-- a carga passa a existir no Mercado de Cargas automaticamente.
-- Não dependemos de uma lista fechada de cargas.

CREATE OR REPLACE FUNCTION truckhub_cargo_key(p_cargo TEXT)
RETURNS TEXT
LANGUAGE plpgsql
AS $$
DECLARE k TEXT;
BEGIN
  k := lower(trim(translate(coalesce(p_cargo,''), 'áàãâäéèêëíìîïóòõôöúùûüçÁÀÃÂÄÉÈÊËÍÌÎÏÓÒÕÔÖÚÙÛÜÇ', 'aaaaaeeeeiiiiooooouuuucAAAAAEEEEIIIIOOOOOUUUUC')));
  k := regexp_replace(k, '[^a-z0-9]+', '_', 'g');
  k := regexp_replace(k, '^_+|_+$', '', 'g');
  RETURN left(coalesce(nullif(k,''),'carga_geral'),70);
END;
$$;

CREATE OR REPLACE FUNCTION truckhub_discover_trip_cargo()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
DECLARE
  k TEXT;
  display_name TEXT;
  rate NUMERIC(8,2);
  offer_id UUID;
BEGIN
  IF NEW.cargo IS NULL OR trim(NEW.cargo) = '' THEN RETURN NEW; END IF;
  k := truckhub_cargo_key(NEW.cargo);
  display_name := left(trim(NEW.cargo),180);
  rate := round((4 + mod(abs(('x' || substr(md5(k),1,8))::bit(32)::int),11) * 0.2)::numeric,2);

  INSERT INTO cargo_market_offers(cargo_key,display_name,rate_brl_km,market_status,discovered_count,last_discovered_at)
  VALUES(k,display_name,rate,CASE WHEN rate>=5.2 THEN 'high' WHEN rate<=4.4 THEN 'low' ELSE 'normal' END,1,NOW())
  ON CONFLICT(cargo_key) DO UPDATE SET
    discovered_count=cargo_market_offers.discovered_count+1,
    last_discovered_at=NOW(),
    active=TRUE,
    updated_at=NOW();

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

DROP TRIGGER IF EXISTS trg_trips_discover_cargo ON trips;
CREATE TRIGGER trg_trips_discover_cargo
BEFORE INSERT ON trips
FOR EACH ROW EXECUTE FUNCTION truckhub_discover_trip_cargo();

CREATE OR REPLACE FUNCTION truckhub_sync_trip_contract()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
  IF NEW.status='finished' AND OLD.status<>'finished' THEN
    UPDATE cargo_contracts
       SET status='delivered',
           delivered_at=COALESCE(delivered_at,NOW()),
           origin=COALESCE(NEW.origin,origin),
           destination=COALESCE(NEW.destination,destination),
           distance_km=COALESCE(NEW.distance_km,distance_km),
           cargo_mass_kg=COALESCE(NEW.cargo_mass_kg,cargo_mass_kg)
     WHERE trip_id=NEW.id;
  ELSE
    UPDATE cargo_contracts
       SET origin=COALESCE(NEW.origin,origin),
           destination=COALESCE(NEW.destination,destination),
           distance_km=COALESCE(NEW.distance_km,distance_km),
           cargo_mass_kg=COALESCE(NEW.cargo_mass_kg,cargo_mass_kg)
     WHERE trip_id=NEW.id;
  END IF;
  RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_trips_sync_cargo_contract ON trips;
CREATE TRIGGER trg_trips_sync_cargo_contract
AFTER UPDATE ON trips
FOR EACH ROW EXECUTE FUNCTION truckhub_sync_trip_contract();
