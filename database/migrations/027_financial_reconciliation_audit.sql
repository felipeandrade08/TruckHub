-- TruckHub — trilha interna de auditoria da reconciliação financeira.
-- Migration consolidada: incorpora o antigo 027_financial_reconciliation_logs.sql.
-- Não contém dados de cartão. Registra somente resultado operacional.

CREATE TABLE IF NOT EXISTS financial_reconciliation_runs (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  started_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  finished_at TIMESTAMPTZ,
  status TEXT NOT NULL DEFAULT 'running',
  scanned_count INTEGER NOT NULL DEFAULT 0,
  candidates_count INTEGER NOT NULL DEFAULT 0,
  repaired_count INTEGER NOT NULL DEFAULT 0,
  already_ok_count INTEGER NOT NULL DEFAULT 0,
  failed_count INTEGER NOT NULL DEFAULT 0,
  error_detail TEXT,
  CONSTRAINT financial_reconciliation_runs_status_check
    CHECK (status IN ('running', 'completed', 'failed')),
  CONSTRAINT financial_reconciliation_runs_counts_check
    CHECK (
      scanned_count >= 0 AND
      candidates_count >= 0 AND
      repaired_count >= 0 AND
      already_ok_count >= 0 AND
      failed_count >= 0
    )
);

CREATE TABLE IF NOT EXISTS financial_reconciliation_items (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  run_id UUID NOT NULL REFERENCES financial_reconciliation_runs(id) ON DELETE CASCADE,
  payment_id UUID NOT NULL REFERENCES payments(id) ON DELETE CASCADE,
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  action TEXT NOT NULL,
  error_detail TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_financial_reconciliation_runs_started
  ON financial_reconciliation_runs(started_at DESC);

CREATE INDEX IF NOT EXISTS idx_financial_reconciliation_items_run
  ON financial_reconciliation_items(run_id, created_at);

CREATE INDEX IF NOT EXISTS idx_financial_reconciliation_items_payment
  ON financial_reconciliation_items(payment_id, created_at DESC);

CREATE OR REPLACE FUNCTION truckhub_start_reconciliation_run()
RETURNS UUID
LANGUAGE plpgsql
AS $$
DECLARE
  v_lock BIGINT := hashtextextended('truckhub-financial-reconciliation', 0);
  v_run UUID;
BEGIN
  IF NOT pg_try_advisory_lock(v_lock) THEN
    RAISE EXCEPTION 'reconciliation_already_running';
  END IF;

  INSERT INTO financial_reconciliation_runs(status)
  VALUES ('running')
  RETURNING id INTO v_run;

  PERFORM set_config('truckhub.reconciliation.lock', v_lock::TEXT, true);
  RETURN v_run;
END;
$$;

CREATE OR REPLACE FUNCTION truckhub_finish_reconciliation_run(
  p_run_id UUID,
  p_status TEXT,
  p_scanned INTEGER,
  p_repaired INTEGER,
  p_failed INTEGER,
  p_error TEXT DEFAULT NULL
)
RETURNS VOID
LANGUAGE plpgsql
AS $$
DECLARE
  v_lock BIGINT := hashtextextended('truckhub-financial-reconciliation', 0);
BEGIN
  UPDATE financial_reconciliation_runs
  SET finished_at = NOW(),
      status = p_status,
      scanned_count = p_scanned,
      candidates_count = p_scanned,
      repaired_count = p_repaired,
      failed_count = p_failed,
      error_detail = LEFT(p_error, 500)
  WHERE id = p_run_id;

  PERFORM pg_advisory_unlock(v_lock);
END;
$$;
