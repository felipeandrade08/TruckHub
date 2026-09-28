-- TransPoli — company split + company ledger are one immutable TripId transaction.
CREATE OR REPLACE FUNCTION apply_company_trip_settlement(
  p_trip_id UUID, p_company_id UUID, p_user_id UUID, p_employment_type TEXT,
  p_gross_revenue NUMERIC, p_driver_share_pct NUMERIC, p_driver_gross NUMERIC,
  p_company_share NUMERIC, p_driver_expenses NUMERIC, p_company_expenses NUMERIC,
  p_driver_net NUMERIC
) RETURNS SETOF company_trip_settlements
LANGUAGE plpgsql AS $$
DECLARE
  settled company_trip_settlements%ROWTYPE;
BEGIN
  SELECT * INTO settled FROM company_trip_settlements
   WHERE trip_id=p_trip_id AND user_id=p_user_id FOR UPDATE;

  IF NOT FOUND THEN
    INSERT INTO company_trip_settlements(
      trip_id,company_id,user_id,employment_type,gross_revenue,driver_share_pct,
      driver_gross,company_share,driver_expenses,company_expenses,loan_payment,driver_net
    ) VALUES(
      p_trip_id,p_company_id,p_user_id,p_employment_type,p_gross_revenue,p_driver_share_pct,
      p_driver_gross,p_company_share,p_driver_expenses,p_company_expenses,0,p_driver_net
    ) RETURNING * INTO settled;
  END IF;

  -- Always reconcile the deterministic ledger from the persisted settlement.
  -- A retry can therefore repair data created before this migration.
  INSERT INTO company_ledger(company_id,user_id,trip_id,transaction_key,type,amount,note)
    VALUES(settled.company_id,settled.user_id,settled.trip_id,'trip-share-'||settled.trip_id,
      'trip.company_share',settled.company_share,'Participação da empresa na viagem')
    ON CONFLICT(company_id,transaction_key) DO NOTHING;

  IF settled.company_expenses>0 THEN
    INSERT INTO company_ledger(company_id,user_id,trip_id,transaction_key,type,amount,note)
      VALUES(settled.company_id,settled.user_id,settled.trip_id,'trip-expenses-'||settled.trip_id,
        'trip.company_expenses',-settled.company_expenses,'Custos operacionais assumidos pela empresa')
      ON CONFLICT(company_id,transaction_key) DO NOTHING;
  END IF;

  RETURN NEXT settled;
END $$;
