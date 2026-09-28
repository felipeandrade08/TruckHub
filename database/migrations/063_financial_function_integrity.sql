-- TransPoli — financial function integrity / repair guard.
-- Re-assert the signatures consumed by the API and fail the migration if either
-- transactional fuel function is missing after all prior migrations.

DO $$
BEGIN
  IF to_regprocedure('public.apply_fuel_payment(uuid,uuid,text,numeric,text,jsonb)') IS NULL THEN
    RAISE EXCEPTION 'missing apply_fuel_payment(uuid,uuid,text,numeric,text,jsonb)';
  END IF;
  IF to_regprocedure('public.apply_company_fuel_expense(uuid,uuid,uuid,text,numeric,text,jsonb)') IS NULL THEN
    RAISE EXCEPTION 'missing apply_company_fuel_expense(uuid,uuid,uuid,text,numeric,text,jsonb)';
  END IF;
END $$;
