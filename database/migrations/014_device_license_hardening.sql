-- TruckHub V1 — proteção de vínculo licença ↔ dispositivo.
-- Garante um único dispositivo ativo por licença e facilita auditoria/validação.

CREATE UNIQUE INDEX IF NOT EXISTS uq_devices_one_active_per_license
  ON devices(license_id)
  WHERE status = 'active';

CREATE UNIQUE INDEX IF NOT EXISTS uq_devices_one_active_id
  ON devices(device_id)
  WHERE status = 'active';

CREATE INDEX IF NOT EXISTS idx_devices_license_status
  ON devices(license_id, status);

CREATE INDEX IF NOT EXISTS idx_devices_last_seen
  ON devices(last_seen_at);

ALTER TABLE devices
  ADD CONSTRAINT devices_device_id_length
  CHECK (char_length(device_id) BETWEEN 16 AND 255) NOT VALID;
