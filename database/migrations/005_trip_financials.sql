-- TruckHub V1 — resultado financeiro automático da viagem.
-- O valor da carga vem do campo Income do job ETS2 quando disponível.
-- Despesas são os lançamentos reais do motorista; TruckHub nunca inventa preços.
-- Migration consolidada: incorpora o antigo 005_cargo_value.sql.

ALTER TABLE trips
  ADD COLUMN IF NOT EXISTS cargo_value_brl NUMERIC(14,2);

CREATE INDEX IF NOT EXISTS idx_expenses_trip_type
  ON expenses(trip_id, type);

CREATE INDEX IF NOT EXISTS idx_trips_user_status_started
  ON trips(user_id, status, started_at DESC);
