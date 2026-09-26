-- TransPoli v1.0.31 — rebalanceamento do Mercado de Cargas.
-- A faixa anterior (R$ 5–12/km) ficou abaixo do custo operacional observado.
-- Contratos já aceitos permanecem congelados: somente o catálogo/ofertas futuras é normalizado.

DO $$
DECLARE c RECORD;
BEGIN
  FOR c IN
    SELECT conrelid::regclass AS table_name, conname
    FROM pg_constraint
    WHERE conrelid IN ('cargo_market_offers'::regclass, 'cargo_contracts'::regclass)
      AND contype='c' AND pg_get_constraintdef(oid) ILIKE '%rate_brl_km%'
  LOOP
    EXECUTE format('ALTER TABLE %s DROP CONSTRAINT IF EXISTS %I',c.table_name,c.conname);
  END LOOP;
END $$;

UPDATE cargo_market_offers
SET rate_brl_km = CASE
  WHEN rate_brl_km IS NULL OR rate_brl_km::text='NaN' THEN 12
  WHEN rate_brl_km < 12 THEN 12
  WHEN rate_brl_km > 22 THEN 22
  ELSE rate_brl_km
END,
market_status = CASE
  WHEN GREATEST(12,LEAST(22,COALESCE(rate_brl_km,12))) >= 18 THEN 'high'
  WHEN GREATEST(12,LEAST(22,COALESCE(rate_brl_km,12))) <= 14 THEN 'low'
  ELSE 'normal'
END,
updated_at=NOW();

UPDATE cargo_rates
SET rate_brl_km = CASE
  WHEN rate_brl_km IS NULL OR rate_brl_km::text='NaN' THEN 12
  WHEN rate_brl_km < 12 THEN 12
  WHEN rate_brl_km > 22 THEN 22
  ELSE rate_brl_km
END,
updated_at=NOW();

ALTER TABLE cargo_market_offers
  ADD CONSTRAINT cargo_market_offers_rate_brl_km_range CHECK(rate_brl_km BETWEEN 12 AND 22);

-- Contratos existentes não são reprecificados. A constraint aceita o legado já
-- congelado e a nova faixa; a API garante 12–22 para novos contratos.
ALTER TABLE cargo_contracts
  ADD CONSTRAINT cargo_contracts_rate_brl_km_positive CHECK(rate_brl_km > 0 AND rate_brl_km <= 22);
