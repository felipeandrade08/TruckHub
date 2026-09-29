-- TransPoli — harden the settlement completion marker.
-- A trip is complete only after its durable account credit exists and, when the
-- driver belongs to a company, the company split has also been persisted.
CREATE OR REPLACE FUNCTION mark_trip_settlement_complete(p_user_id UUID,p_trip_id UUID)
RETURNS BOOLEAN LANGUAGE plpgsql AS $$
BEGIN
  IF NOT EXISTS(
    SELECT 1 FROM trips t
     WHERE t.id=p_trip_id AND t.user_id=p_user_id AND t.status='finished'
  ) THEN RETURN FALSE; END IF;

  IF NOT EXISTS(
    SELECT 1 FROM economy_ledger el
     WHERE el.user_id=p_user_id AND el.trip_id=p_trip_id AND el.entry_type='trip_income'
  ) THEN RETURN FALSE; END IF;

  IF EXISTS(
    SELECT 1 FROM company_members cm
     WHERE cm.user_id=p_user_id AND cm.status='active'
       AND cm.employment_type IN ('aggregate','company_driver')
  ) AND NOT EXISTS(
    SELECT 1 FROM company_trip_settlements cs
     WHERE cs.trip_id=p_trip_id AND cs.user_id=p_user_id
  ) THEN RETURN FALSE; END IF;

  IF EXISTS(
    SELECT 1 FROM company_loan_repayments r
     WHERE r.trip_id=p_trip_id AND r.user_id<>p_user_id
  ) OR EXISTS(
    SELECT 1 FROM economy_loan_trip_repayments r
     WHERE r.trip_id=p_trip_id AND r.user_id<>p_user_id
  ) THEN RETURN FALSE; END IF;

  INSERT INTO trip_settlement_completions(trip_id,user_id,core_settled_at,loans_reconciled_at)
  VALUES(p_trip_id,p_user_id,NOW(),NOW())
  ON CONFLICT(trip_id) DO UPDATE SET
    loans_reconciled_at=EXCLUDED.loans_reconciled_at,
    completed_at=EXCLUDED.loans_reconciled_at
  WHERE trip_settlement_completions.user_id=EXCLUDED.user_id;
  RETURN TRUE;
END $$;
