-- TruckHub / TransPoli — Fase D: Mercado de Cargas + contratos de viagem
-- O preço/km pertence ao mercado interno. A rota/origem/destino/distância continuam
-- sendo preenchidos pela entrega detectada no ETS2/ATS quando disponíveis.

CREATE TABLE IF NOT EXISTS cargo_market_offers (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  cargo_key VARCHAR(80) NOT NULL,
  display_name VARCHAR(180) NOT NULL,
  rate_brl_km NUMERIC(8,2) NOT NULL CHECK (rate_brl_km >= 4 AND rate_brl_km <= 6),
  market_status VARCHAR(20) NOT NULL DEFAULT 'normal' CHECK (market_status IN ('high','normal','low')),
  active BOOLEAN NOT NULL DEFAULT TRUE,
  discovered_count INTEGER NOT NULL DEFAULT 0 CHECK (discovered_count >= 0),
  last_discovered_at TIMESTAMPTZ,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(cargo_key)
);

CREATE TABLE IF NOT EXISTS cargo_contracts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  offer_id UUID REFERENCES cargo_market_offers(id) ON DELETE SET NULL,
  cargo_key VARCHAR(80) NOT NULL,
  cargo VARCHAR(180) NOT NULL,
  rate_brl_km NUMERIC(8,2) NOT NULL CHECK (rate_brl_km >= 4 AND rate_brl_km <= 6),
  status VARCHAR(20) NOT NULL DEFAULT 'accepted' CHECK (status IN ('accepted','active','delivered','cancelled')),
  origin VARCHAR(180),
  destination VARCHAR(180),
  distance_km NUMERIC(12,2) CHECK (distance_km IS NULL OR distance_km >= 0),
  cargo_mass_kg NUMERIC(12,2) CHECK (cargo_mass_kg IS NULL OR cargo_mass_kg >= 0),
  bonus_brl NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (bonus_brl >= 0),
  damage_penalty_pct NUMERIC(6,2) NOT NULL DEFAULT 15 CHECK (damage_penalty_pct >= 0 AND damage_penalty_pct <= 100),
  clean_delivery_bonus_pct NUMERIC(6,2) NOT NULL DEFAULT 5 CHECK (clean_delivery_bonus_pct >= 0 AND clean_delivery_bonus_pct <= 100),
  accepted_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  started_at TIMESTAMPTZ,
  delivered_at TIMESTAMPTZ,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_cargo_contracts_user_status ON cargo_contracts(user_id,status,accepted_at DESC);
CREATE INDEX IF NOT EXISTS idx_cargo_contracts_trip ON cargo_contracts(trip_id);

ALTER TABLE trips ADD COLUMN IF NOT EXISTS cargo_contract_id UUID REFERENCES cargo_contracts(id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS idx_trips_cargo_contract ON trips(cargo_contract_id);

-- Catálogo inicial: o sistema pode descobrir novas cargas em runtime.
INSERT INTO cargo_market_offers(cargo_key,display_name,rate_brl_km,market_status)
VALUES
 ('milho','Milho',4.20,'normal'),
 ('soja','Soja',4.40,'normal'),
 ('carvao','Carvão',4.60,'normal'),
 ('veiculos','Automóveis',4.80,'normal'),
 ('pesada','Máquinas e equipamentos',5.20,'normal'),
 ('especial','Carga especial',5.60,'normal'),
 ('refrigerada','Carga refrigerada',5.00,'normal'),
 ('perigosa','Carga perigosa',5.40,'normal'),
 ('madeira','Madeira',4.40,'normal'),
 ('minerais','Minerais',4.80,'normal'),
 ('construcao','Construção',4.60,'normal'),
 ('agricola','Agrícola',4.20,'normal'),
 ('eletronicos','Eletrônicos',5.00,'normal'),
 ('industrial','Industrial',5.20,'normal'),
 ('logistica','Logística',4.00,'normal')
ON CONFLICT(cargo_key) DO UPDATE SET active=TRUE,display_name=EXCLUDED.display_name;
