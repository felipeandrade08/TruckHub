-- Idempotent, atomic repayment for company-funded loans.
CREATE TABLE IF NOT EXISTS company_loan_repayments (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  company_id UUID NOT NULL REFERENCES companies(id) ON DELETE CASCADE,
  loan_id UUID NOT NULL REFERENCES company_loans(id) ON DELETE CASCADE,
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  trip_id UUID NOT NULL REFERENCES trips(id) ON DELETE CASCADE,
  amount NUMERIC(14,2) NOT NULL CHECK (amount > 0),
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE(loan_id,trip_id)
);
CREATE INDEX IF NOT EXISTS idx_company_loan_repayments_company ON company_loan_repayments(company_id,created_at DESC);

CREATE OR REPLACE FUNCTION apply_company_loan_trip_repayment(
  p_loan_id UUID, p_user_id UUID, p_trip_id UUID, p_requested NUMERIC
) RETURNS NUMERIC LANGUAGE plpgsql AS $$
DECLARE
  l company_loans%ROWTYPE;
  existing NUMERIC;
  pay NUMERIC;
  current_balance NUMERIC;
  new_balance NUMERIC;
  new_paid NUMERIC;
BEGIN
  SELECT amount INTO existing FROM company_loan_repayments WHERE loan_id=p_loan_id AND trip_id=p_trip_id;
  IF FOUND THEN RETURN existing; END IF;

  SELECT * INTO l FROM company_loans WHERE id=p_loan_id AND user_id=p_user_id AND status='active' FOR UPDATE;
  IF NOT FOUND THEN RETURN 0; END IF;

  SELECT amount INTO existing FROM company_loan_repayments WHERE loan_id=p_loan_id AND trip_id=p_trip_id;
  IF FOUND THEN RETURN existing; END IF;

  pay := LEAST(GREATEST(ROUND(p_requested,2),0), GREATEST(l.total_due-l.paid_amount,0));
  IF pay <= 0 THEN RETURN 0; END IF;

  INSERT INTO economy_accounts(user_id,balance_brl) VALUES(p_user_id,0) ON CONFLICT(user_id) DO NOTHING;
  SELECT balance_brl INTO current_balance FROM economy_accounts WHERE user_id=p_user_id FOR UPDATE;
  IF current_balance < pay THEN pay := GREATEST(current_balance,0); END IF;
  IF pay <= 0 THEN RETURN 0; END IF;

  new_balance := ROUND(current_balance-pay,2);
  new_paid := ROUND(l.paid_amount+pay,2);

  INSERT INTO company_loan_repayments(company_id,loan_id,user_id,trip_id,amount)
  VALUES(l.company_id,l.id,p_user_id,p_trip_id,pay);

  UPDATE economy_accounts SET balance_brl=new_balance,updated_at=NOW() WHERE user_id=p_user_id;
  UPDATE company_loans SET paid_amount=new_paid,status=CASE WHEN new_paid>=total_due THEN 'paid' ELSE 'active' END,
    closed_at=CASE WHEN new_paid>=total_due THEN NOW() ELSE NULL END WHERE id=l.id;

  INSERT INTO economy_ledger(user_id,trip_id,entry_type,description,amount_brl,balance_after_brl,metadata)
  VALUES(p_user_id,p_trip_id,'company_loan_payment','Parcela do empréstimo TransPoli',-pay,new_balance,
    jsonb_build_object('companyLoanId',l.id,'repaymentId',l.id::text||':'||p_trip_id::text));

  INSERT INTO company_ledger(company_id,user_id,trip_id,transaction_key,type,amount,note)
  VALUES(l.company_id,p_user_id,p_trip_id,'loan-payment-'||l.id::text||'-'||p_trip_id::text,'loan.repayment',pay,'Parcela recebida no fechamento da viagem')
  ON CONFLICT(company_id,transaction_key) DO NOTHING;

  UPDATE company_trip_settlements SET loan_payment=pay,driver_net=GREATEST(0,driver_net-pay) WHERE trip_id=p_trip_id;
  RETURN pay;
END $$;
