-- TransPoli V1.0.9 — economia do motorista + garagem exclusiva.
-- Valores são de simulação interna do TruckHub e podem ser ajustados pelo administrador.

CREATE TABLE IF NOT EXISTS economy_settings (
  id BOOLEAN PRIMARY KEY DEFAULT TRUE CHECK (id),
  fuel_price_brl NUMERIC(10,2) NOT NULL DEFAULT 5.98 CHECK (fuel_price_brl >= 0),
  minimum_margin_pct NUMERIC(6,2) NOT NULL DEFAULT 20 CHECK (minimum_margin_pct >= 0),
  maintenance_pct_of_revenue NUMERIC(6,2) NOT NULL DEFAULT 6 CHECK (maintenance_pct_of_revenue >= 0),
  efficiency_bonus_pct NUMERIC(6,2) NOT NULL DEFAULT 5 CHECK (efficiency_bonus_pct >= 0),
  clean_delivery_bonus_pct NUMERIC(6,2) NOT NULL DEFAULT 5 CHECK (clean_delivery_bonus_pct >= 0),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
INSERT INTO economy_settings(id) VALUES(TRUE) ON CONFLICT(id) DO NOTHING;

CREATE TABLE IF NOT EXISTS cargo_rates (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  cargo_key VARCHAR(40) NOT NULL UNIQUE,
  display_name VARCHAR(100) NOT NULL,
  rate_brl_km NUMERIC(10,2) NOT NULL CHECK (rate_brl_km >= 0),
  active BOOLEAN NOT NULL DEFAULT TRUE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO cargo_rates(cargo_key,display_name,rate_brl_km) VALUES
 ('default','Carga geral',2.80),
 ('milho','Milho',3.20),
 ('soja','Soja',3.40),
 ('carvao','Carvão',3.80),
 ('veiculos','Veículos',4.00),
 ('pesada','Carga pesada',4.40),
 ('especial','Carga especial',5.00)
ON CONFLICT(cargo_key) DO NOTHING;

CREATE TABLE IF NOT EXISTS economy_accounts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE RESTRICT,
  balance_brl NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (balance_brl >= 0),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS economy_ledger (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  entry_type VARCHAR(40) NOT NULL,
  description VARCHAR(255) NOT NULL,
  amount_brl NUMERIC(14,2) NOT NULL,
  balance_after_brl NUMERIC(14,2) NOT NULL CHECK (balance_after_brl >= 0),
  metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_economy_trip_income ON economy_ledger(trip_id, entry_type) WHERE trip_id IS NOT NULL AND entry_type='trip_income';
CREATE INDEX IF NOT EXISTS idx_economy_ledger_user_created ON economy_ledger(user_id,created_at DESC);

CREATE TABLE IF NOT EXISTS economy_loans (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  principal_brl NUMERIC(14,2) NOT NULL CHECK (principal_brl > 0),
  remaining_brl NUMERIC(14,2) NOT NULL CHECK (remaining_brl >= 0),
  repayment_pct NUMERIC(6,2) NOT NULL DEFAULT 20 CHECK (repayment_pct > 0 AND repayment_pct <= 100),
  status VARCHAR(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','paid','cancelled')),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  paid_at TIMESTAMPTZ
);
CREATE UNIQUE INDEX IF NOT EXISTS uq_economy_one_active_loan ON economy_loans(user_id) WHERE status='active';
CREATE INDEX IF NOT EXISTS idx_economy_loans_user ON economy_loans(user_id,created_at DESC);

CREATE TABLE IF NOT EXISTS garage_assignments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  truck_id UUID NOT NULL UNIQUE REFERENCES trucks(id) ON DELETE CASCADE,
  exclusive BOOLEAN NOT NULL DEFAULT TRUE,
  skin_code VARCHAR(120),
  label VARCHAR(160),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_garage_user ON garage_assignments(user_id);
