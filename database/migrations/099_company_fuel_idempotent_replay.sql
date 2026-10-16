-- A3: atomic company-paid fuel recovery after a lost HTTP acknowledgement.
-- Keep the original transaction key; serialize retries before touching expenses.
CREATE OR REPLACE FUNCTION apply_company_fuel_expense(
  p_company_id UUID, p_user_id UUID, p_trip_id UUID, p_source_key TEXT,
  p_amount NUMERIC, p_description TEXT, p_metadata JSONB
) RETURNS TABLE(expense_id UUID, balance_brl NUMERIC, duplicate BOOLEAN)
LANGUAGE plpgsql AS $$
DECLARE
  existing expenses%ROWTYPE;
  created expenses%ROWTYPE;
  current_balance NUMERIC;
  fuel_key TEXT;
BEGIN
  IF p_company_id IS NULL OR p_user_id IS NULL OR p_source_key IS NULL
     OR BTRIM(p_source_key)='' OR p_amount IS NULL OR p_amount<=0 THEN
    RAISE EXCEPTION 'company_fuel_invalid_arguments';
  END IF;

  fuel_key := 'fuel-event-' || p_source_key;
  -- Serialize same-company/same-event retries, including concurrent HTTP requests.
  PERFORM pg_advisory_xact_lock(hashtextextended(p_company_id::text || ':' || fuel_key, 0));

  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0)
    ON CONFLICT(user_id) DO NOTHING;
  SELECT ea.balance_brl INTO current_balance
    FROM economy_accounts AS ea WHERE ea.user_id=p_user_id;

  IF EXISTS (
    SELECT 1 FROM company_ledger AS cl
    WHERE cl.company_id=p_company_id AND cl.transaction_key=fuel_key
  ) THEN
    SELECT e.* INTO existing FROM expenses AS e
     WHERE e.user_id=p_user_id AND e.type='fuel'
       AND e.trip_id IS NOT DISTINCT FROM p_trip_id
       AND e.description=p_description AND e.amount=p_amount
     ORDER BY e.created_at ASC LIMIT 1;
    RETURN QUERY SELECT existing.id,current_balance,TRUE;
    RETURN;
  END IF;

  -- Both inserts belong to the same database transaction. A failed ledger
  -- insert rolls back the expense instead of leaving an orphan charge.
  INSERT INTO expenses(user_id,trip_id,type,description,amount)
    VALUES(p_user_id,p_trip_id,'fuel',p_description,p_amount) RETURNING * INTO created;
  INSERT INTO company_ledger(company_id,user_id,trip_id,transaction_key,type,amount,note)
    VALUES(p_company_id,p_user_id,p_trip_id,fuel_key,
      'fuel.company_assumed',0,'Abastecimento assumido pela empresa; consolidado no acerto da viagem');
  RETURN QUERY SELECT created.id,current_balance,FALSE;
END $$;
