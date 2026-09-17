-- TruckHub / TransPoli - FASE J: Estatísticas
-- Agrega índices para consultas de histórico e estatísticas por motorista.
CREATE INDEX IF NOT EXISTS idx_trips_user_status_finished_at ON trips(user_id,status,finished_at DESC);
CREATE INDEX IF NOT EXISTS idx_trips_user_distance ON trips(user_id,distance_km);
CREATE INDEX IF NOT EXISTS idx_trips_user_fuel ON trips(user_id,fuel_used_l);
CREATE INDEX IF NOT EXISTS idx_expenses_user_created_at ON expenses(user_id,created_at DESC);
CREATE INDEX IF NOT EXISTS idx_economy_ledger_user_created_at ON economy_ledger(user_id,created_at DESC);
