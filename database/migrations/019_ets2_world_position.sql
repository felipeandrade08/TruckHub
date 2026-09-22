-- TransPoli: posição/orientação do caminhão vinda diretamente do ETS2 Telemetry SDK.
-- Não é latitude/longitude; são coordenadas do mundo do jogo + orientação.
ALTER TABLE device_telemetry_latest ADD COLUMN IF NOT EXISTS world_x DOUBLE PRECISION NOT NULL DEFAULT 0;
ALTER TABLE device_telemetry_latest ADD COLUMN IF NOT EXISTS world_y DOUBLE PRECISION NOT NULL DEFAULT 0;
ALTER TABLE device_telemetry_latest ADD COLUMN IF NOT EXISTS world_z DOUBLE PRECISION NOT NULL DEFAULT 0;
ALTER TABLE device_telemetry_latest ADD COLUMN IF NOT EXISTS heading_deg DOUBLE PRECISION NOT NULL DEFAULT 0;
ALTER TABLE device_telemetry_latest ADD COLUMN IF NOT EXISTS pitch_deg DOUBLE PRECISION NOT NULL DEFAULT 0;
ALTER TABLE device_telemetry_latest ADD COLUMN IF NOT EXISTS roll_deg DOUBLE PRECISION NOT NULL DEFAULT 0;
ALTER TABLE device_telemetry_latest ADD COLUMN IF NOT EXISTS position_valid BOOLEAN NOT NULL DEFAULT FALSE;
