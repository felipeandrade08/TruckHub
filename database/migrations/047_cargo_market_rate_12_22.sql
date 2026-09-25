-- TransPoli — faixa única do Mercado de Cargas: R$ 12,00 a R$ 22,00/km.
-- Recalibra ofertas existentes e substitui a regra/constraint anterior de 5–12.

ALTER TABLE cargo_market_offers
  DROP CONSTRAINT IF EXISTS cargo_market_offers_rate_range;

UPDATE cargo_market_offers
SET rate_brl_km = ROUND((12 + (rate_brl_km - 5) * (10.0 / 7.0))::numeric, 2),
    updated_at = NOW()
WHERE rate_brl_km < 12 OR rate_brl_km > 22 OR rate_brl_km BETWEEN 5 AND 12;

UPDATE cargo_market_offers
SET rate_brl_km = LEAST(22, GREATEST(12, rate_brl_km)),
    status = CASE
      WHEN LEAST(22, GREATEST(12, rate_brl_km)) >= 19 THEN 'high'
      WHEN LEAST(22, GREATEST(12, rate_brl_km)) <= 14 THEN 'low'
      ELSE 'normal'
    END,
    updated_at = NOW();

ALTER TABLE cargo_market_offers
  ADD CONSTRAINT cargo_market_offers_rate_range
  CHECK (rate_brl_km >= 12 AND rate_brl_km <= 22);

CREATE OR REPLACE FUNCTION truckhub_discover_cargo_offer()
RETURNS TRIGGER AS $$
DECLARE
  cargo_key TEXT;
  hash_number BIGINT;
  rate NUMERIC(10,2);
BEGIN
  cargo_key := truckhub_cargo_key(NEW.cargo);
  IF cargo_key IS NULL OR cargo_key = '' THEN
    RETURN NEW;
  END IF;

  hash_number := abs(('x' || substr(md5(cargo_key), 1, 15))::bit(60)::bigint);
  rate := round((12 + mod(hash_number, 21) * 0.5)::numeric, 2);

  INSERT INTO cargo_market_offers(cargo_key,cargo_name,category,rate_brl_km,status,active,updated_at)
  VALUES(
    cargo_key,
    NEW.cargo,
    'Carga ETS2',
    rate,
    CASE WHEN rate >= 19 THEN 'high' WHEN rate <= 14 THEN 'low' ELSE 'normal' END,
    TRUE,
    NOW()
  )
  ON CONFLICT(cargo_key) DO UPDATE SET
    cargo_name = EXCLUDED.cargo_name,
    rate_brl_km = LEAST(22, GREATEST(12, cargo_market_offers.rate_brl_km)),
    status = CASE
      WHEN LEAST(22, GREATEST(12, cargo_market_offers.rate_brl_km)) >= 19 THEN 'high'
      WHEN LEAST(22, GREATEST(12, cargo_market_offers.rate_brl_km)) <= 14 THEN 'low'
      ELSE 'normal'
    END,
    active = TRUE,
    updated_at = NOW();

  RETURN NEW;
END;
$$ LANGUAGE plpgsql;
