CREATE OR REPLACE FUNCTION approve_company_loan(p_loan_id UUID,p_company_id UUID)
RETURNS TABLE(loan JSONB,driver_balance NUMERIC) LANGUAGE plpgsql AS $$
DECLARE l company_loans%ROWTYPE; company_balance NUMERIC; before_balance NUMERIC; after_balance NUMERIC;
BEGIN
 SELECT * INTO l FROM company_loans WHERE id=p_loan_id AND company_id=p_company_id FOR UPDATE;
 IF NOT FOUND OR l.status<>'pending' THEN RAISE EXCEPTION 'loan_already_decided'; END IF;
 SELECT COALESCE(SUM(amount),0) INTO company_balance FROM company_ledger WHERE company_id=p_company_id;
 IF company_balance<l.principal THEN RAISE EXCEPTION 'company_balance_insufficient'; END IF;
 INSERT INTO economy_accounts(user_id,balance_brl) VALUES(l.user_id,0) ON CONFLICT(user_id) DO NOTHING;
 SELECT balance_brl INTO before_balance FROM economy_accounts WHERE user_id=l.user_id FOR UPDATE;
 after_balance:=ROUND(before_balance+l.principal,2);
 UPDATE economy_accounts SET balance_brl=after_balance,updated_at=NOW() WHERE user_id=l.user_id;
 INSERT INTO economy_ledger(user_id,entry_type,description,amount_brl,balance_after_brl,metadata)
 VALUES(l.user_id,'company_loan_credit','Crédito concedido pela TransPoli',l.principal,after_balance,jsonb_build_object('companyLoanId',l.id));
 INSERT INTO company_ledger(company_id,user_id,transaction_key,type,amount,note)
 VALUES(p_company_id,l.user_id,'loan-disbursement-'||l.id::text,'loan.disbursement',-l.principal,'Empréstimo concedido ao motorista')
 ON CONFLICT(company_id,transaction_key) DO NOTHING;
 UPDATE company_loans SET status='active',approved_at=NOW() WHERE id=l.id;
 SELECT * INTO l FROM company_loans WHERE id=l.id;
 RETURN QUERY SELECT to_jsonb(l),after_balance;
END $$;
