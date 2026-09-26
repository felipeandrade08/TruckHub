-- TransPoli v1.0.31 — abastecimento assumido pela empresa.
-- Registra a despesa da viagem de forma idempotente sem debitar a conta pessoal
-- do motorista. O custo entra no company_trip_settlement/company_ledger no fechamento.
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

  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;
  SELECT balance_brl INTO current_balance FROM economy_accounts WHERE user_id=p_user_id;

  SELECT * INTO existing FROM expenses
   WHERE user_id=p_user_id AND type='fuel'
     AND trip_id IS NOT DISTINCT FROM p_trip_id
     AND description=p_description AND amount=p_amount
   ORDER BY created_at DESC LIMIT 1;

  -- sourceKey is persisted in a zero-value company ledger marker so retries never
  -- create a second expense, while the actual company cost remains consolidated
  -- once by apply_company_trip_settlement.
  IF EXISTS(
    SELECT 1 FROM company_ledger
    WHERE company_id=p_company_id AND transaction_key='fuel-event-'||p_source_key
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
