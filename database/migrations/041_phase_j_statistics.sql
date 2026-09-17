-- TruckHub / TransPoli — FASE J: Estatísticas
-- Somente índices de leitura; não cria dados fictícios nem altera a versão do produto.
CREATE INDEX IF NOT EXISTS idx_trips_user_finished_at ON trips(user_id,status,finished_at DESC);
CREATE INDEX IF NOT EXISTS idx_trips_user_distance ON trips(user_id,distance_km);
CREATE INDEX IF NOT EXISTS idx_trips_user_fuel ON trips(user_id,fuel_used_l);
CREATE INDEX IF NOT EXISTS idx_trips_user_cargo ON trips(user_id,cargo);
CREATE INDEX IF NOT EXISTS idx_expenses_user_created_type ON expenses(user_id,created_at DESC,type);
CREATE INDEX IF NOT EXISTS idx_transpoli_events_user_occurred ON transpoli_operational_events(user_id,occurred_at DESC,event_type);
