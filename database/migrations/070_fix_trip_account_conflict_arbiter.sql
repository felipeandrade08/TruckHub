-- TransPoli — estabiliza a identidade da conta econômica e remove a dependência
-- de ON CONFLICT(user_id) no settlement legado.
-- Falha de forma segura se houver duplicatas históricas; não deduplica saldos.
CREATE UNIQUE INDEX IF NOT EXISTS uq_economy_accounts_user_id
  ON economy_accounts(user_id);

-- TransPoli — corrige ambiguidade PL/pgSQL no settlement atômico de viagem.
-- Recria a função existente sem alterar dados; aliases explícitos evitam conflito
-- entre a coluna economy_accounts.balance_brl e o parâmetro de saída balance_brl.
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
  IF NOT EXISTS (SELECT 1 FROM economy_accounts ea WHERE ea.user_id=p_user_id) THEN
    INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0);
  END IF;
  SELECT ea.balance_brl INTO current_balance FROM economy_accounts ea WHERE ea.user_id=p_user_id FOR UPDATE;

  IF EXISTS(SELECT 1 FROM economy_ledger el WHERE el.user_id=p_user_id AND el.trip_id=p_trip_id AND el.entry_type='trip_income') THEN
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

  UPDATE economy_accounts ea SET balance_brl=final_balance,updated_at=NOW() WHERE ea.user_id=p_user_id;
  RETURN QUERY SELECT TRUE,current_balance,final_balance;
END $$;

