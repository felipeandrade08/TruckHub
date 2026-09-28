-- TransPoli — atomic/idempotent repayment for legacy personal economy loans.
CREATE TABLE IF NOT EXISTS economy_loan_trip_repayments(
 id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
 loan_id UUID NOT NULL REFERENCES economy_loans(id) ON DELETE CASCADE,
 user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 trip_id UUID NOT NULL REFERENCES trips(id) ON DELETE CASCADE,
 amount NUMERIC(14,2) NOT NULL CHECK(amount>0),
 created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
 UNIQUE(loan_id,trip_id)
);
CREATE OR REPLACE FUNCTION apply_economy_loan_trip_repayment(
 p_loan_id UUID,p_user_id UUID,p_trip_id UUID,p_requested NUMERIC
) RETURNS NUMERIC LANGUAGE plpgsql AS $$
DECLARE l economy_loans%ROWTYPE; existing NUMERIC; pay NUMERIC; bal NUMERIC; newbal NUMERIC; newremaining NUMERIC; newpaid INTEGER;
BEGIN
 SELECT amount INTO existing FROM economy_loan_trip_repayments WHERE loan_id=p_loan_id AND trip_id=p_trip_id;
 IF FOUND THEN RETURN existing; END IF;
 SELECT * INTO l FROM economy_loans WHERE id=p_loan_id AND user_id=p_user_id AND status='active' FOR UPDATE;
 IF NOT FOUND THEN RETURN 0; END IF;
 SELECT amount INTO existing FROM economy_loan_trip_repayments WHERE loan_id=p_loan_id AND trip_id=p_trip_id;
 IF FOUND THEN RETURN existing; END IF;
 INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;
 SELECT balance_brl INTO bal FROM economy_accounts WHERE user_id=p_user_id FOR UPDATE;
 pay:=LEAST(GREATEST(ROUND(COALESCE(p_requested,0),2),0),GREATEST(l.remaining_brl,0),GREATEST(bal,0));
 IF pay<=0 THEN RETURN 0; END IF;
 newbal:=ROUND(bal-pay,2); newremaining:=ROUND(GREATEST(l.remaining_brl-pay,0),2); newpaid:=l.installments_paid+1;
 INSERT INTO economy_loan_trip_repayments(loan_id,user_id,trip_id,amount) VALUES(l.id,p_user_id,p_trip_id,pay);
 UPDATE economy_accounts SET balance_brl=newbal,updated_at=NOW() WHERE user_id=p_user_id;
 UPDATE economy_loans SET remaining_brl=newremaining,installments_paid=newpaid,
  status=CASE WHEN newremaining<=0 THEN 'paid' ELSE 'active' END,
  closed_at=CASE WHEN newremaining<=0 THEN NOW() ELSE NULL END WHERE id=l.id;
 INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
 VALUES(p_user_id,p_trip_id,'loan_payment','Parcela automática do empréstimo',-pay,newbal,
  jsonb_build_object('loanId',l.id,'repaymentId',l.id::text||':'||p_trip_id::text));
 RETURN pay;
END $$;
