-- TransPoli 2.0 — liquidação empresarial idempotente por viagem.
CREATE TABLE IF NOT EXISTS company_trip_settlements (
  trip_id UUID PRIMARY KEY REFERENCES trips(id) ON DELETE CASCADE,
  company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  employment_type TEXT NOT NULL,
  gross_revenue NUMERIC(14,2) NOT NULL,
  driver_share_pct NUMERIC(5,2) NOT NULL,
  driver_gross NUMERIC(14,2) NOT NULL,
  company_share NUMERIC(14,2) NOT NULL,
  driver_expenses NUMERIC(14,2) NOT NULL DEFAULT 0,
  company_expenses NUMERIC(14,2) NOT NULL DEFAULT 0,
  loan_payment NUMERIC(14,2) NOT NULL DEFAULT 0,
  driver_net NUMERIC(14,2) NOT NULL,
  settled_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT company_trip_settlement_employment CHECK(employment_type IN ('aggregate','company_driver'))
);
CREATE INDEX IF NOT EXISTS idx_company_trip_settlements_company ON company_trip_settlements(company_id,settled_at DESC);
CREATE INDEX IF NOT EXISTS idx_company_trip_settlements_user ON company_trip_settlements(user_id,settled_at DESC);
