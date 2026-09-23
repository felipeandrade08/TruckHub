-- TransPoli 2.0 — vínculo profissional, crachá e economia empresarial.
-- Não usa dinheiro do ETS2: toda a contabilidade abaixo pertence exclusivamente ao TransPoli.

ALTER TABLE company_members
  ADD COLUMN IF NOT EXISTS employment_type TEXT,
  ADD COLUMN IF NOT EXISTS registration_number TEXT,
  ADD COLUMN IF NOT EXISTS badge_issued_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS employment_selected_at TIMESTAMPTZ;

UPDATE company_members
SET employment_type=COALESCE(employment_type,'pending')
WHERE role='driver';

ALTER TABLE company_members
  DROP CONSTRAINT IF EXISTS company_members_employment_type_check;
ALTER TABLE company_members
  ADD CONSTRAINT company_members_employment_type_check
  CHECK (employment_type IS NULL OR employment_type IN ('pending','aggregate','company_driver'));

CREATE UNIQUE INDEX IF NOT EXISTS uq_company_members_registration
  ON company_members(company_id,registration_number)
  WHERE registration_number IS NOT NULL;

CREATE TABLE IF NOT EXISTS company_financial_policy (
  company_id UUID PRIMARY KEY REFERENCES companies(id) ON DELETE CASCADE,
  aggregate_driver_share NUMERIC(5,2) NOT NULL DEFAULT 80.00,
  company_driver_share NUMERIC(5,2) NOT NULL DEFAULT 35.00,
  aggregate_fuel_payer TEXT NOT NULL DEFAULT 'driver',
  aggregate_maintenance_payer TEXT NOT NULL DEFAULT 'driver',
  company_driver_fuel_payer TEXT NOT NULL DEFAULT 'company',
  company_driver_maintenance_payer TEXT NOT NULL DEFAULT 'company',
  loan_interest_rate NUMERIC(6,3) NOT NULL DEFAULT 10.000,
  loan_repayment_percent NUMERIC(5,2) NOT NULL DEFAULT 15.00,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT company_policy_aggregate_share CHECK (aggregate_driver_share BETWEEN 0 AND 100),
  CONSTRAINT company_policy_employee_share CHECK (company_driver_share BETWEEN 0 AND 100),
  CONSTRAINT company_policy_interest CHECK (loan_interest_rate BETWEEN 0 AND 100),
  CONSTRAINT company_policy_repayment CHECK (loan_repayment_percent BETWEEN 0 AND 100),
  CONSTRAINT company_policy_payers CHECK (
    aggregate_fuel_payer IN ('driver','company') AND
    aggregate_maintenance_payer IN ('driver','company') AND
    company_driver_fuel_payer IN ('driver','company') AND
    company_driver_maintenance_payer IN ('driver','company')
  )
);

INSERT INTO company_financial_policy(company_id)
SELECT id FROM companies
ON CONFLICT(company_id) DO NOTHING;

CREATE TABLE IF NOT EXISTS company_ledger (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  user_id UUID REFERENCES users(id) ON DELETE SET NULL,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  transaction_key TEXT NOT NULL,
  type TEXT NOT NULL,
  amount NUMERIC(14,2) NOT NULL,
  note TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(company_id,transaction_key)
);
CREATE INDEX IF NOT EXISTS idx_company_ledger_company_date ON company_ledger(company_id,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_company_ledger_user ON company_ledger(company_id,user_id,created_at DESC);

CREATE TABLE IF NOT EXISTS company_loans (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  principal NUMERIC(14,2) NOT NULL CHECK (principal>0),
  interest_rate NUMERIC(6,3) NOT NULL CHECK (interest_rate>=0),
  total_due NUMERIC(14,2) NOT NULL CHECK (total_due>0),
  paid_amount NUMERIC(14,2) NOT NULL DEFAULT 0 CHECK (paid_amount>=0),
  repayment_percent NUMERIC(5,2) NOT NULL CHECK (repayment_percent BETWEEN 0 AND 100),
  status TEXT NOT NULL DEFAULT 'pending',
  requested_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  approved_at TIMESTAMPTZ,
  closed_at TIMESTAMPTZ,
  CONSTRAINT company_loans_status CHECK(status IN ('pending','approved','active','paid','rejected','cancelled'))
);
CREATE INDEX IF NOT EXISTS idx_company_loans_company_status ON company_loans(company_id,status,requested_at DESC);
CREATE INDEX IF NOT EXISTS idx_company_loans_user ON company_loans(user_id,status,requested_at DESC);

-- Gera crachá/registro para motoristas já vinculados que ainda não possuem um.
WITH numbered AS (
  SELECT company_id,user_id,
         ROW_NUMBER() OVER (PARTITION BY company_id ORDER BY joined_at,user_id) AS n
  FROM company_members
  WHERE role='driver' AND registration_number IS NULL
)
UPDATE company_members cm
SET registration_number='TP-DRV-'||LPAD(numbered.n::text,6,'0'),
    badge_issued_at=COALESCE(cm.badge_issued_at,NOW())
FROM numbered
WHERE cm.company_id=numbered.company_id AND cm.user_id=numbered.user_id;
