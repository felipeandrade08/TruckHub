-- TransPoli — idempotent/atomic registration of one physical ETS2 toll event.
CREATE OR REPLACE FUNCTION record_toll_event(
  p_user_id UUID,p_trip_id UUID,p_source_key TEXT,p_description TEXT,p_metadata JSONB
) RETURNS TABLE(event_id UUID,balance_brl NUMERIC,duplicate BOOLEAN)
LANGUAGE plpgsql AS $$
DECLARE existing economy_ledger%ROWTYPE; current_balance NUMERIC; created economy_ledger%ROWTYPE;
BEGIN
  IF p_source_key IS NULL OR BTRIM(p_source_key)='' THEN RAISE EXCEPTION 'toll_source_key_required'; END IF;
  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;
  SELECT balance_brl INTO current_balance FROM economy_accounts WHERE user_id=p_user_id FOR UPDATE;
  SELECT * INTO existing FROM economy_ledger
    WHERE user_id=p_user_id AND entry_type='toll_event' AND metadata->>'sourceKey'=p_source_key LIMIT 1;
  IF FOUND THEN RETURN QUERY SELECT existing.id,current_balance,TRUE; RETURN; END IF;
  INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
    VALUES(p_user_id,p_trip_id,'toll_event',p_description,0,current_balance,p_metadata)
    RETURNING * INTO created;
  RETURN QUERY SELECT created.id,current_balance,FALSE;
END $$;
