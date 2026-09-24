-- TransPoli — atomic/idempotent repayment for legacy economy_loans by TripId.
CREATE OR REPLACE FUNCTION apply_economy_loan_trip_repayment(
  p_loan_id UUID, p_user_id UUID, p_trip_id UUID, p_requested NUMERIC
) RETURNS NUMERIC LANGUAGE plpgsql AS $$
DECLARE
  l economy_loans%ROWTYPE;
  existing NUMERIC;
  current_balance NUMERIC;
  pay NUMERIC;
  new_balance NUMERIC;
  remaining NUMERIC;
  paid_count INTEGER;
BEGIN
  SELECT ABS(amount_brl) INTO existing FROM economy_ledger
   WHERE user_id=p_user_id AND trip_id=p_trip_id AND entry_type='loan_payment' LIMIT 1;
  IF FOUND THEN RETURN existing; END IF;

  SELECT * INTO l FROM economy_loans
   WHERE id=p_loan_id AND user_id=p_user_id AND status='active' FOR UPDATE;
  IF NOT FOUND THEN RETURN 0; END IF;

  SELECT ABS(amount_brl) INTO existing FROM economy_ledger
   WHERE user_id=p_user_id AND trip_id=p_trip_id AND entry_type='loan_payment' LIMIT 1;
  IF FOUND THEN RETURN existing; END IF;

  pay:=LEAST(GREATEST(ROUND(p_requested,2),0),GREATEST(l.remaining_brl,0));
  IF pay<=0 THEN RETURN 0; END IF;

  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;
  SELECT balance_brl INTO current_balance FROM economy_accounts WHERE user_id=p_user_id FOR UPDATE;
  pay:=LEAST(pay,GREATEST(current_balance,0));
  IF pay<=0 THEN RETURN 0; END IF;

  new_balance:=ROUND(current_balance-pay,2);
  remaining:=ROUND(GREATEST(l.remaining_brl-pay,0),2);
  paid_count:=LEAST(l.installments_total,l.installments_paid+1);

  UPDATE economy_accounts SET balance_brl=new_balance,updated_at=NOW() WHERE user_id=p_user_id;
  UPDATE economy_loans SET remaining_brl=remaining,installments_paid=paid_count,
    status=CASE WHEN remaining<=0 THEN 'paid' ELSE 'active' END,
    paid_at=CASE WHEN remaining<=0 THEN NOW() ELSE NULL END WHERE id=l.id;
  INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
   VALUES(p_user_id,p_trip_id,'loan_payment','Parcela '||paid_count||'/'||l.installments_total||' do emprestimo',
     -pay,new_balance,jsonb_build_object('loanId',l.id,'repaymentPct',l.repayment_pct,'remainingBrl',remaining));
  RETURN pay;
END $$;
