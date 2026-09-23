-- Registro profissional concorrente e permanente.
CREATE SEQUENCE IF NOT EXISTS company_driver_registration_seq START WITH 1000;

CREATE OR REPLACE FUNCTION assign_company_driver_registration()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF NEW.role='driver' AND NEW.registration_number IS NULL THEN
    NEW.registration_number := 'TP-DRV-' || LPAD(nextval('company_driver_registration_seq')::text,6,'0');
    NEW.badge_issued_at := COALESCE(NEW.badge_issued_at,NOW());
    NEW.employment_type := COALESCE(NEW.employment_type,'pending');
  END IF;
  RETURN NEW;
END $$;

DROP TRIGGER IF EXISTS trg_company_driver_registration ON company_members;
CREATE TRIGGER trg_company_driver_registration
BEFORE INSERT OR UPDATE OF role,registration_number ON company_members
FOR EACH ROW EXECUTE FUNCTION assign_company_driver_registration();

UPDATE company_members
SET registration_number=NULL
WHERE role='driver' AND registration_number IS NULL;
