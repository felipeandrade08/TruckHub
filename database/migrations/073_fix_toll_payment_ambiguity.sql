-- TransPoli — fix PL/pgSQL output-column ambiguity in PoliPass transactions.
-- Existing sourceKey values remain unchanged so already queued toll payments can
-- be retried safely after this migration.

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

  INSERT INTO economy_accounts(user_id,balance_brl)
    VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;

  SELECT ea.balance_brl INTO current_balance
    FROM economy_accounts AS ea
   WHERE ea.user_id=p_user_id
   FOR UPDATE;

  SELECT el.* INTO existing
    FROM economy_ledger AS el
   WHERE el.user_id=p_user_id
     AND el.entry_type='toll_payment'
     AND el.metadata->>'sourceKey'=p_source_key
   LIMIT 1;

  IF FOUND THEN
    SELECT e.* INTO expense
      FROM expenses AS e
     WHERE e.user_id=p_user_id
       AND e.type='toll'
       AND e.trip_id IS NOT DISTINCT FROM existing.trip_id
       AND e.description=existing.description
       AND e.amount=ABS(existing.amount_brl)
     ORDER BY e.created_at ASC
     LIMIT 1;

    RETURN QUERY SELECT existing.id,expense.id,current_balance,TRUE;
    RETURN;
  END IF;

  new_balance:=ROUND(current_balance-p_amount_brl,2);

  INSERT INTO expenses(user_id,trip_id,type,description,amount)
    VALUES(p_user_id,p_trip_id,'toll',p_description,p_amount_brl)
    RETURNING * INTO expense;

  UPDATE economy_accounts AS ea
     SET balance_brl=new_balance,updated_at=NOW()
   WHERE ea.user_id=p_user_id;

  INSERT INTO economy_ledger(
    user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata
  )
    VALUES(
      p_user_id,p_trip_id,'toll_payment',p_description,-p_amount_brl,new_balance,p_metadata
    )
    RETURNING * INTO existing;

  RETURN QUERY SELECT existing.id,expense.id,new_balance,FALSE;
END $$;
