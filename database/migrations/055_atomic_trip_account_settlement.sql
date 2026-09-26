-- TransPoli — atomic account/ledger settlement for one immutable TripId.
CREATE OR REPLACE FUNCTION apply_trip_account_settlement(
  p_user_id UUID, p_trip_id UUID, p_gross NUMERIC, p_expenses NUMERIC,
  p_income_description TEXT, p_income_metadata JSONB, p_expense_metadata JSONB
) RETURNS TABLE(applied BOOLEAN, opening_balance NUMERIC, balance_brl NUMERIC)
LANGUAGE plpgsql AS $$
DECLARE
  current_balance NUMERIC;
  after_income NUMERIC;
  final_balance NUMERIC;
BEGIN
  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;
  SELECT balance_brl INTO current_balance FROM economy_accounts WHERE user_id=p_user_id FOR UPDATE;

  IF EXISTS(SELECT 1 FROM economy_ledger WHERE user_id=p_user_id AND trip_id=p_trip_id AND entry_type='trip_income') THEN
    RETURN QUERY SELECT FALSE,current_balance,current_balance;
    RETURN;
  END IF;

  after_income:=ROUND(current_balance+ROUND(GREATEST(COALESCE(p_gross,0),0),2),2);
  final_balance:=ROUND(after_income-ROUND(GREATEST(COALESCE(p_expenses,0),0),2),2);

  -- The unique trip_income constraint is the durable identity barrier. All writes
  -- below share this PostgreSQL transaction and roll back together on failure.
  INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
    VALUES(p_user_id,p_trip_id,'trip_income',p_income_description,ROUND(GREATEST(COALESCE(p_gross,0),0),2),after_income,COALESCE(p_income_metadata,'{}'::jsonb));

  IF COALESCE(p_expenses,0)>0 THEN
    INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
      VALUES(p_user_id,p_trip_id,'trip_expenses','Despesas registradas da viagem',-ROUND(p_expenses,2),final_balance,COALESCE(p_expense_metadata,'{}'::jsonb));
  END IF;

  UPDATE economy_accounts SET balance_brl=final_balance,updated_at=NOW() WHERE user_id=p_user_id;
  RETURN QUERY SELECT TRUE,current_balance,final_balance;
END $$;
