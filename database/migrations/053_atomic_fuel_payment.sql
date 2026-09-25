-- TransPoli — atomic/idempotent processing of a physical refuel event.
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

  -- Serialize all balance mutations for this account.
  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;
  SELECT balance_brl INTO current_balance FROM economy_accounts WHERE user_id=p_user_id FOR UPDATE;

  SELECT * INTO existing FROM economy_ledger
   WHERE user_id=p_user_id AND entry_type='fuel_payment'
     AND metadata->>'sourceKey'=p_source_key LIMIT 1;
  IF FOUND THEN
    SELECT * INTO expense FROM expenses
     WHERE user_id=p_user_id AND type='fuel' AND trip_id IS NOT DISTINCT FROM existing.trip_id
       AND description=existing.description AND amount=ABS(existing.amount_brl)
     ORDER BY created_at ASC LIMIT 1;
    RETURN QUERY SELECT expense.id,current_balance,TRUE;
    RETURN;
  END IF;

  new_balance:=ROUND(current_balance-p_amount,2);
  INSERT INTO expenses(user_id,trip_id,type,description,amount)
    VALUES(p_user_id,p_trip_id,'fuel',p_description,p_amount) RETURNING * INTO expense;
  UPDATE economy_accounts SET balance_brl=new_balance,updated_at=NOW() WHERE user_id=p_user_id;
  INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
    VALUES(p_user_id,p_trip_id,'fuel_payment',p_description,-p_amount,new_balance,p_metadata);
  RETURN QUERY SELECT expense.id,new_balance,FALSE;
END $$;
