-- TransPoli — durable marker for the complete financial pipeline of one TripId.
CREATE TABLE IF NOT EXISTS trip_settlement_completions (
  trip_id UUID PRIMARY KEY REFERENCES trips(id) ON DELETE CASCADE,
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  core_settled_at TIMESTAMPTZ NOT NULL,
  loans_reconciled_at TIMESTAMPTZ NOT NULL,
  completed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_trip_settlement_completions_user
  ON trip_settlement_completions(user_id,completed_at DESC);

CREATE OR REPLACE FUNCTION mark_trip_settlement_complete(p_user_id UUID,p_trip_id UUID)
RETURNS BOOLEAN LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS(
    SELECT 1 FROM economy_ledger
    WHERE user_id=p_user_id AND trip_id=p_trip_id AND entry_type='trip_income'
  ) THEN
    RETURN FALSE;
  END IF;
  INSERT INTO trip_settlement_completions(trip_id,user_id,core_settled_at,loans_reconciled_at)
  VALUES(p_trip_id,p_user_id,NOW(),NOW())
  ON CONFLICT(trip_id) DO UPDATE SET
    loans_reconciled_at=EXCLUDED.loans_reconciled_at,
    completed_at=EXCLUDED.loans_reconciled_at
  WHERE trip_settlement_completions.user_id=EXCLUDED.user_id;
  RETURN TRUE;
END $$;
