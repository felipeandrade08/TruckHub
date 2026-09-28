-- TransPoli — atomic/idempotent PoliPass debit in BRL.
-- The source ETS2 EUR amount and FX audit data live only in metadata; operational
-- balance, expense and ledger values are always BRL.
CREATE OR REPLACE FUNCTION apply_toll_payment(
  p_user_id UUID, p_trip_id UUID, p_source_key TEXT, p_amount_brl NUMERIC,
  p_description TEXT, p_metadata JSONB
) RETURNS TABLE(event_id UUID, expense_id UUID, balance_brl NUMERIC, duplicate BOOLEAN)
LANGUAGE plpgsql AS $$
DECLARE
  existing economy_ledger%ROWTYPE;
  expense expenses%ROWTYPE;
  current_balance NUMERIC;
  new_balance NUMERIC;
BEGIN
  IF p_source_key IS NULL OR BTRIM(p_source_key)='' OR p_amount_brl IS NULL OR p_amount_brl<=0 THEN
    RAISE EXCEPTION 'toll_payment_invalid_arguments';
  END IF;

  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;
  SELECT balance_brl INTO current_balance FROM economy_accounts WHERE user_id=p_user_id FOR UPDATE;

  SELECT * INTO existing FROM economy_ledger
    WHERE user_id=p_user_id AND entry_type='toll_payment'
      AND metadata->>'sourceKey'=p_source_key LIMIT 1;
  IF FOUND THEN
    SELECT * INTO expense FROM expenses
      WHERE user_id=p_user_id AND type='toll'
        AND trip_id IS NOT DISTINCT FROM existing.trip_id
        AND description=existing.description AND amount=ABS(existing.amount_brl)
      ORDER BY created_at ASC LIMIT 1;
    RETURN QUERY SELECT existing.id,expense.id,current_balance,TRUE;
    RETURN;
  END IF;

  new_balance:=ROUND(current_balance-p_amount_brl,2);
  INSERT INTO expenses(user_id,trip_id,type,description,amount)
    VALUES(p_user_id,p_trip_id,'toll',p_description,p_amount_brl) RETURNING * INTO expense;
  UPDATE economy_accounts SET balance_brl=new_balance,updated_at=NOW() WHERE user_id=p_user_id;
  INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
    VALUES(p_user_id,p_trip_id,'toll_payment',p_description,-p_amount_brl,new_balance,p_metadata)
    RETURNING * INTO existing;
  RETURN QUERY SELECT existing.id,expense.id,new_balance,FALSE;
END $$;
