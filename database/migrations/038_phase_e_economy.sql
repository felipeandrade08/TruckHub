-- TruckHub / TransPoli — FASE E: Economia
-- Consolida a economia interna sem depender de APIs externas.
-- A viagem continua sendo a origem da receita; despesas e empréstimos entram no livro-caixa.

ALTER TABLE IF EXISTS economy_loans
  ADD COLUMN IF NOT EXISTS installments_total INTEGER NOT NULL DEFAULT 10 CHECK (installments_total > 0),
  ADD COLUMN IF NOT EXISTS installments_paid INTEGER NOT NULL DEFAULT 0 CHECK (installments_paid >= 0),
  ADD COLUMN IF NOT EXISTS installment_min_brl NUMERIC(14,2) NOT NULL DEFAULT 100 CHECK (installment_min_brl >= 0),
  ADD COLUMN IF NOT EXISTS repayment_pct NUMERIC(6,2) NOT NULL DEFAULT 20 CHECK (repayment_pct > 0 AND repayment_pct <= 100);

CREATE INDEX IF NOT EXISTS idx_economy_ledger_trip_type ON economy_ledger(trip_id, entry_type, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_expenses_user_trip_type ON expenses(user_id, trip_id, type, created_at DESC);

-- Garantia de idempotência para receitas de viagens.
CREATE UNIQUE INDEX IF NOT EXISTS uq_economy_trip_income
  ON economy_ledger(trip_id, entry_type)
  WHERE trip_id IS NOT NULL AND entry_type='trip_income';


ALTER TABLE IF EXISTS economy_loans ADD COLUMN IF NOT EXISTS interest_rate_monthly_pct NUMERIC(6,3) NOT NULL DEFAULT 0, ADD COLUMN IF NOT EXISTS total_payable_brl NUMERIC(14,2) NOT NULL DEFAULT 0;
