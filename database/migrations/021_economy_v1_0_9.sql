-- TruckHub V1.0.9 — Economia, banco do motorista e regras de frete.
-- Valores iniciais são configuráveis; não representam preços oficiais.

CREATE TABLE IF NOT EXISTS economy_settings (
  id BOOLEAN PRIMARY KEY DEFAULT TRUE,
  fuel_price_brl NUMERIC(10,3) NOT NULL DEFAULT 5.980,
  minimum_margin_pct NUMERIC(6,2) NOT NULL DEFAULT 20.00,
  maintenance_pct_of_revenue NUMERIC(6,2) NOT NULL DEFAULT 6.00,
  efficiency_bonus_pct NUMERIC(6,2) NOT NULL DEFAULT 5.00,
  clean_delivery_bonus_pct NUMERIC(6,2) NOT NULL DEFAULT 5.00,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO economy_settings(id) VALUES(TRUE) ON CONFLICT(id) DO NOTHING;

CREATE TABLE IF NOT EXISTS cargo_rates (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  cargo_key VARCHAR(80) NOT NULL UNIQUE,
  display_name VARCHAR(120) NOT NULL,
  rate_brl_km NUMERIC(10,2) NOT NULL CHECK (rate_brl_km >= 0),
  active BOOLEAN NOT NULL DEFAULT TRUE,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO cargo_rates(cargo_key,display_name,rate_brl_km) VALUES
 ('default','Carga geral',2.80),
 ('milho','Milho',3.00),
 ('soja','Soja',3.10),
 ('carvao','Carvão',3.20),
 ('veiculos','Veículos',3.40),
 ('pesada','Carga pesada',3.80),
 ('especial','Carga especial',4.20)
ON CONFLICT(cargo_key) DO NOTHING;

CREATE TABLE IF NOT EXISTS economy_accounts (
  user_id UUID PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
  balance_brl NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (balance_brl >= -100000000),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS economy_ledger (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  entry_type VARCHAR(40) NOT NULL,
  description VARCHAR(255) NOT NULL,
  amount_brl NUMERIC(14,2) NOT NULL,
  balance_after_brl NUMERIC(14,2) NOT NULL,
  metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS economy_loans (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  principal_brl NUMERIC(14,2) NOT NULL CHECK (principal_brl > 0),
  remaining_brl NUMERIC(14,2) NOT NULL CHECK (remaining_brl >= 0),
  repayment_pct NUMERIC(6,2) NOT NULL DEFAULT 20.00 CHECK (repayment_pct > 0 AND repayment_pct <= 100),
  status VARCHAR(20) NOT NULL DEFAULT 'active' CHECK (status IN ('active','paid','cancelled')),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  paid_at TIMESTAMPTZ
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_economy_one_open_loan
  ON economy_loans(user_id) WHERE status='active';
CREATE INDEX IF NOT EXISTS idx_economy_ledger_user_created ON economy_ledger(user_id,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_economy_ledger_trip ON economy_ledger(trip_id,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_economy_loans_user_status ON economy_loans(user_id,status);
