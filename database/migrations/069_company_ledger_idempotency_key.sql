-- TransPoli — garante a chave de idempotência exigida pelo ledger empresarial.
-- apply_company_trip_settlement usa ON CONFLICT(company_id,transaction_key);
-- instalações onde company_ledger já existia antes da migration 045 podem não
-- ter recebido a UNIQUE declarada dentro de CREATE TABLE IF NOT EXISTS.
CREATE UNIQUE INDEX IF NOT EXISTS uq_company_ledger_transaction_key
  ON company_ledger(company_id, transaction_key);
