-- TransPoli — reconcile legacy company settlements whose persisted split was
-- calculated from a gross value different from the trip's official cargo_value_brl.
-- The correction is append-only in the driver's ledger and idempotent by TripId.

CREATE OR REPLACE FUNCTION reconcile_company_trip_split(p_user_id UUID,p_trip_id UUID)
RETURNS TABLE(corrected BOOLEAN,driver_gross NUMERIC,company_share NUMERIC,balance_brl NUMERIC)
LANGUAGE plpgsql AS $$
DECLARE
  s company_trip_settlements%ROWTYPE;
  official_gross NUMERIC;
  expected_driver NUMERIC;
  expected_company NUMERIC;
  credited NUMERIC;
  delta NUMERIC;
  current_balance NUMERIC;
  correction_key TEXT;
BEGIN
  SELECT cs.* INTO s
    FROM company_trip_settlements cs
   WHERE cs.trip_id=p_trip_id AND cs.user_id=p_user_id
   FOR UPDATE;
  IF NOT FOUND THEN RETURN QUERY SELECT FALSE,NULL::NUMERIC,NULL::NUMERIC,NULL::NUMERIC; RETURN; END IF;

  SELECT ROUND(COALESCE(t.cargo_value_brl,0),2) INTO official_gross
    FROM trips t WHERE t.id=p_trip_id AND t.user_id=p_user_id;
  IF official_gross IS NULL OR official_gross<=0 THEN
    RETURN QUERY SELECT FALSE,s.driver_gross,s.company_share,NULL::NUMERIC; RETURN;
  END IF;

  expected_driver:=ROUND(official_gross*GREATEST(0,LEAST(100,s.driver_share_pct))/100,2);
  expected_company:=ROUND(official_gross-expected_driver,2);
  correction_key:='company-split-correction:'||p_trip_id::text;

  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0)
    ON CONFLICT(user_id) DO NOTHING;
  SELECT ea.balance_brl INTO current_balance FROM economy_accounts ea
   WHERE ea.user_id=p_user_id FOR UPDATE;

  SELECT COALESCE(SUM(el.amount_brl),0) INTO credited
    FROM economy_ledger el
   WHERE el.user_id=p_user_id AND el.trip_id=p_trip_id
     AND el.entry_type IN ('trip_income','company_split_correction');

  delta:=ROUND(expected_driver-credited,2);
  IF delta<>0 AND NOT EXISTS(
    SELECT 1 FROM economy_ledger el
     WHERE el.user_id=p_user_id AND el.trip_id=p_trip_id
       AND el.entry_type='company_split_correction'
       AND el.metadata->>'sourceKey'=correction_key
  ) THEN
    current_balance:=ROUND(current_balance+delta,2);
    INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
      VALUES(p_user_id,p_trip_id,'company_split_correction',
        'Reconciliação da participação motorista/empresa',delta,current_balance,
        jsonb_build_object('sourceKey',correction_key,'officialGross',official_gross,
          'driverSharePct',s.driver_share_pct,'expectedDriverGross',expected_driver,
          'previousDriverGross',s.driver_gross));
    UPDATE economy_accounts ea SET balance_brl=current_balance,updated_at=NOW()
      WHERE ea.user_id=p_user_id;
  END IF;

  UPDATE company_trip_settlements cs SET
      gross_revenue=official_gross,
      driver_gross=expected_driver,
      company_share=expected_company,
      driver_net=ROUND(expected_driver-COALESCE(cs.driver_expenses,0)-COALESCE(cs.loan_payment,0),2)
    WHERE cs.trip_id=p_trip_id AND cs.user_id=p_user_id
      AND (cs.gross_revenue<>official_gross OR cs.driver_gross<>expected_driver OR cs.company_share<>expected_company);

  UPDATE company_ledger cl SET amount=expected_company
    WHERE cl.company_id=s.company_id AND cl.trip_id=p_trip_id
      AND cl.transaction_key='trip-share-'||p_trip_id::text;

  RETURN QUERY SELECT (delta<>0 OR s.gross_revenue<>official_gross OR s.driver_gross<>expected_driver OR s.company_share<>expected_company),
    expected_driver,expected_company,current_balance;
END $$;
