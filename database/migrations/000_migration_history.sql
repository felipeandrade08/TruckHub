-- TruckHub V1 — controle das migrations aplicadas.
-- Execute antes das migrations numeradas em um banco novo.

CREATE TABLE IF NOT EXISTS truckhub_schema_migrations (
  version VARCHAR(120) PRIMARY KEY,
  applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE truckhub_schema_migrations IS 'Controle das migrations SQL aplicadas no banco TruckHub.';
