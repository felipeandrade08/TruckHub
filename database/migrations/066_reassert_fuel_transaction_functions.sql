-- TransPoli — production repair guard for fuel transactions.
-- Re-applies the corrected, fully-qualified PL/pgSQL definitions so an environment
-- that recorded an older migration state cannot keep serving the ambiguous function.

CREATE OR REPLACE FUNCTION apply_fuel_payment(
  p_user_id UUID, p_trip_id UUID, p_source_key TEXT, p_amount NUMERIC,
  p_description TEXT, p_metadata JSONB
) RETURNS TABLE(expense_id UUID, balance_brl NUMERIC, duplicate BOOLEAN)
LANGUAGE plpgsql AS $$
DECLARE
  existing economy_ledger%ROWTYPE;
  expense expenses%ROWTYPE;
  current_balance NUMERIC;
  new_balance NUMERIC;
BEGIN
  IF p_source_key IS NULL OR BTRIM(p_source_key)='' OR p_amount IS NULL OR p_amount<=0 THEN
    RAISE EXCEPTION 'fuel_payment_invalid_arguments';
  END IF;

  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0)
    ON CONFLICT(user_id) DO NOTHING;
  SELECT ea.balance_brl INTO current_balance
    FROM economy_accounts AS ea WHERE ea.user_id=p_user_id FOR UPDATE;

  SELECT el.* INTO existing FROM economy_ledger AS el
   WHERE el.user_id=p_user_id AND el.entry_type='fuel_payment'
     AND el.metadata->>'sourceKey'=p_source_key LIMIT 1;
  IF FOUND THEN
    SELECT e.* INTO expense FROM expenses AS e
     WHERE e.user_id=p_user_id AND e.type='fuel'
       AND e.trip_id IS NOT DISTINCT FROM existing.trip_id
       AND e.description=existing.description AND e.amount=ABS(existing.amount_brl)
     ORDER BY e.created_at ASC LIMIT 1;
    RETURN QUERY SELECT expense.id,current_balance,TRUE;
    RETURN;
  END IF;

  new_balance:=ROUND(current_balance-p_amount,2);
  INSERT INTO expenses(user_id,trip_id,type,description,amount)
    VALUES(p_user_id,p_trip_id,'fuel',p_description,p_amount) RETURNING * INTO expense;
  UPDATE economy_accounts AS ea
    SET balance_brl=new_balance,updated_at=NOW() WHERE ea.user_id=p_user_id;
  INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
    VALUES(p_user_id,p_trip_id,'fuel_payment',p_description,-p_amount,new_balance,p_metadata);
  RETURN QUERY SELECT expense.id,new_balance,FALSE;
END $$;

CREATE OR REPLACE FUNCTION apply_company_fuel_expense(
  p_company_id UUID, p_user_id UUID, p_trip_id UUID, p_source_key TEXT,
  p_amount NUMERIC, p_description TEXT, p_metadata JSONB
) RETURNS TABLE(expense_id UUID, balance_brl NUMERIC, duplicate BOOLEAN)
LANGUAGE plpgsql AS $$
DECLARE
  existing expenses%ROWTYPE;
  created expenses%ROWTYPE;
  current_balance NUMERIC;
BEGIN
  IF p_company_id IS NULL OR p_source_key IS NULL OR BTRIM(p_source_key)='' OR p_amount IS NULL OR p_amount<=0 THEN
    RAISE EXCEPTION 'company_fuel_invalid_arguments';
  END IF;

  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0)
    ON CONFLICT(user_id) DO NOTHING;
  SELECT ea.balance_brl INTO current_balance
    FROM economy_accounts AS ea WHERE ea.user_id=p_user_id;

  SELECT e.* INTO existing FROM expenses AS e
   WHERE e.user_id=p_user_id AND e.type='fuel'
     AND e.trip_id IS NOT DISTINCT FROM p_trip_id
     AND e.description=p_description AND e.amount=p_amount
   ORDER BY e.created_at DESC LIMIT 1;

  IF EXISTS(
    SELECT 1 FROM company_ledger AS cl
    WHERE cl.company_id=p_company_id AND cl.transaction_key='fuel-event-'||p_source_key
  ) THEN
    RETURN QUERY SELECT existing.id,current_balance,TRUE;
    RETURN;
  END IF;

  INSERT INTO expenses(user_id,trip_id,type,description,amount)
    VALUES(p_user_id,p_trip_id,'fuel',p_description,p_amount) RETURNING * INTO created;
  INSERT INTO company_ledger(company_id,user_id,trip_id,transaction_key,type,amount,note)
    VALUES(p_company_id,p_user_id,p_trip_id,'fuel-event-'||p_source_key,
      'fuel.company_assumed',0,'Abastecimento assumido pela empresa; consolidado no acerto da viagem')
    ON CONFLICT(company_id,transaction_key) DO NOTHING;
  RETURN QUERY SELECT created.id,current_balance,FALSE;
END $$;

DO $$
BEGIN
  IF to_regprocedure('public.apply_fuel_payment(uuid,uuid,text,numeric,text,jsonb)') IS NULL THEN
    RAISE EXCEPTION 'missing apply_fuel_payment';
  END IF;
  IF to_regprocedure('public.apply_company_fuel_expense(uuid,uuid,uuid,text,numeric,text,jsonb)') IS NULL THEN
    RAISE EXCEPTION 'missing apply_company_fuel_expense';
  END IF;
END $$;
