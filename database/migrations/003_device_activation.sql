-- TruckHub V1 — ativação do desktop por e-mail + PIN.
-- A tabela devices já limita uma licença a um único dispositivo por UNIQUE(license_id).
-- Esta migration adiciona auditoria mínima para ativações sem armazenar o PIN.

ALTER TABLE devices
  ADD COLUMN IF NOT EXISTS activated_at TIMESTAMPTZ;

ALTER TABLE devices
  ADD COLUMN IF NOT EXISTS last_ip_hash TEXT;

CREATE INDEX IF NOT EXISTS idx_devices_status ON devices(status);

COMMENT ON COLUMN devices.last_ip_hash IS 'Hash opcional do IP usado na última ativação; nunca armazena o IP em claro.';
