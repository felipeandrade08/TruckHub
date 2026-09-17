-- TransPoli V1.1.0 — Banco do Motorista completo + Garagem exclusiva com bloqueio real.
-- Todos os valores são de simulação interna do TruckHub.

-- ============================================================
-- 1. AVARIA DA CARGA NA VIAGEM (base do bônus de entrega limpa)
-- ============================================================
ALTER TABLE trips
  ADD COLUMN IF NOT EXISTS cargo_damage NUMERIC(6,4) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS cargo_mass_kg NUMERIC(12,2);

DO $$ BEGIN
  ALTER TABLE trips ADD CONSTRAINT trips_cargo_damage_range
    CHECK (cargo_damage >= 0 AND cargo_damage <= 1) NOT VALID;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

-- ============================================================
-- 2. PARÂMETROS DA ECONOMIA
-- ============================================================
ALTER TABLE economy_settings
  ADD COLUMN IF NOT EXISTS weight_surcharge_brl_ton_km NUMERIC(10,4) NOT NULL DEFAULT 0.0150,
  ADD COLUMN IF NOT EXISTS free_weight_tons            NUMERIC(10,2) NOT NULL DEFAULT 20,
  ADD COLUMN IF NOT EXISTS maintenance_brl_km          NUMERIC(10,4) NOT NULL DEFAULT 0.4200,
  ADD COLUMN IF NOT EXISTS efficiency_target_l_km      NUMERIC(10,4) NOT NULL DEFAULT 0.4500,
  ADD COLUMN IF NOT EXISTS damage_tolerance            NUMERIC(6,4)  NOT NULL DEFAULT 0.0100,
  ADD COLUMN IF NOT EXISTS damage_penalty_pct          NUMERIC(6,2)  NOT NULL DEFAULT 15,
  ADD COLUMN IF NOT EXISTS minimum_trip_revenue_brl    NUMERIC(10,2) NOT NULL DEFAULT 150;

-- economy.ts (cargoKey) reconhece "perigosa" e "refrigerada" desde a V1.1.0,
-- mas nenhuma migration anterior cadastrava a tarifa dessas categorias —
-- elas caíam silenciosamente na tarifa padrão. Completa aqui.
INSERT INTO cargo_rates(cargo_key,display_name,rate_brl_km) VALUES
 ('perigosa','Carga perigosa',4.60),
 ('refrigerada','Carga refrigerada',3.60)
ON CONFLICT(cargo_key) DO NOTHING;

-- ============================================================
-- 3. EMPRÉSTIMO COM PARCELAS EXPLÍCITAS
-- ============================================================
ALTER TABLE economy_loans
  ADD COLUMN IF NOT EXISTS installments_total     INTEGER NOT NULL DEFAULT 10,
  ADD COLUMN IF NOT EXISTS installments_paid      INTEGER NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS installment_min_brl    NUMERIC(14,2) NOT NULL DEFAULT 0;

DO $$ BEGIN
  ALTER TABLE economy_loans ADD CONSTRAINT economy_loans_installments_positive
    CHECK (installments_total > 0 AND installments_paid >= 0) NOT VALID;
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

-- ============================================================
-- 4. GARAGEM — CHAVE NORMALIZADA E AUDITORIA DE BLOQUEIO
-- ============================================================
ALTER TABLE garage_assignments
  ADD COLUMN IF NOT EXISTS truck_key   VARCHAR(220),
  ADD COLUMN IF NOT EXISTS active      BOOLEAN NOT NULL DEFAULT TRUE,
  ADD COLUMN IF NOT EXISTS last_seen_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_garage_truck_key ON garage_assignments(truck_key);
CREATE UNIQUE INDEX IF NOT EXISTS uq_garage_user_truck_key
  ON garage_assignments(user_id, truck_key) WHERE truck_key IS NOT NULL;

CREATE TABLE IF NOT EXISTS garage_access_log (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  truck_key VARCHAR(220) NOT NULL,
  brand VARCHAR(80),
  model VARCHAR(120),
  license_plate VARCHAR(32),
  authorized BOOLEAN NOT NULL,
  reason VARCHAR(60) NOT NULL,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_garage_log_user_created
  ON garage_access_log(user_id, created_at DESC);

-- Preenche truck_key dos vínculos já existentes.
UPDATE garage_assignments g
   SET truck_key = LOWER(TRIM(COALESCE(t.brand,'')))||'|'||
                   LOWER(TRIM(COALESCE(t.model,'')))||'|'||
                   LOWER(TRIM(COALESCE(t.license_plate,'')))
  FROM trucks t
 WHERE t.id = g.truck_id AND g.truck_key IS NULL;

-- ============================================================
-- 5. LIVRO-CAIXA — ÍNDICE PARA AGREGAÇÃO DIÁRIA
-- ============================================================
CREATE INDEX IF NOT EXISTS idx_economy_ledger_user_type_created
  ON economy_ledger(user_id, entry_type, created_at DESC);

-- O saldo pode ficar negativo enquanto um empréstimo estiver aberto;
-- a trava antiga impedia o lançamento de despesas maiores que a receita.
DO $$ BEGIN
  ALTER TABLE economy_accounts DROP CONSTRAINT economy_accounts_balance_brl_check;
EXCEPTION WHEN undefined_object THEN NULL; END $$;

DO $$ BEGIN
  ALTER TABLE economy_ledger DROP CONSTRAINT economy_ledger_balance_after_brl_check;
EXCEPTION WHEN undefined_object THEN NULL; END $$;
