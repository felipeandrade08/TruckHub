-- TruckHub V1 — endurecimento das regras de licença.
-- Mantém trial com validade de 7 dias e lifetime sem expiração.
-- A migration é defensiva para poder ser aplicada sobre bancos já existentes.

DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint
    WHERE conname = 'licenses_lifetime_consistency'
  ) THEN
    ALTER TABLE licenses
      ADD CONSTRAINT licenses_lifetime_consistency
      CHECK (
        (license_type = 'lifetime' AND status = 'active' AND expires_at IS NULL)
        OR
        (license_type = 'trial' AND expires_at IS NULL)
      ) NOT VALID;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS idx_licenses_status ON licenses(status);
CREATE INDEX IF NOT EXISTS idx_licenses_trial_expires ON licenses(trial_expires_at);

-- Não altera automaticamente dados existentes. A API continua sendo a autoridade
-- para marcar trials vencidos como expired quando a licença é consultada/ativada.
