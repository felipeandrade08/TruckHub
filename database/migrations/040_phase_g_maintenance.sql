-- TransPoli Etapa 7 — Garagem / Manutenção
-- Histórico de manutenção real, sem filiais e integrado ao livro-caixa.

CREATE TABLE IF NOT EXISTS truck_maintenance_records (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  truck_id UUID NOT NULL REFERENCES trucks(id) ON DELETE CASCADE,
  service_type VARCHAR(60) NOT NULL,
  component VARCHAR(60) NOT NULL,
  description VARCHAR(255),
  cost_brl NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (cost_brl >= 0),
  odometer_km NUMERIC(12,1),
  wear_engine NUMERIC(6,4),
  wear_transmission NUMERIC(6,4),
  wear_cabin NUMERIC(6,4),
  wear_chassis NUMERIC(6,4),
  wear_wheels NUMERIC(6,4),
  source_key VARCHAR(180) UNIQUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_truck_maintenance_user_created
  ON truck_maintenance_records(user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_truck_maintenance_truck_created
  ON truck_maintenance_records(truck_id, created_at DESC);

ALTER TABLE trucks
  ADD COLUMN IF NOT EXISTS last_maintenance_at TIMESTAMPTZ,
  ADD COLUMN IF NOT EXISTS last_maintenance_odometer_km NUMERIC(12,1);

CREATE OR REPLACE FUNCTION truckhub_touch_maintenance()
RETURNS TRIGGER AS $$
BEGIN
  UPDATE trucks
     SET last_maintenance_at = NEW.created_at,
         last_maintenance_odometer_km = COALESCE(NEW.odometer_km, last_maintenance_odometer_km),
         updated_at = NOW()
   WHERE id = NEW.truck_id AND user_id = NEW.user_id;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_truckhub_touch_maintenance ON truck_maintenance_records;
CREATE TRIGGER trg_truckhub_touch_maintenance
AFTER INSERT ON truck_maintenance_records
FOR EACH ROW EXECUTE FUNCTION truckhub_touch_maintenance();
