-- TransPoli — identidade durável client/server para viagens offline-first.
-- Não altera viagens históricas: as novas chaves são preenchidas somente por clientes
-- que as enviarem. Índices parciais preservam compatibilidade com linhas antigas.
ALTER TABLE trips ADD COLUMN IF NOT EXISTS client_trip_id TEXT;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS start_source_key TEXT;
ALTER TABLE trips ADD COLUMN IF NOT EXISTS finish_source_key TEXT;

CREATE UNIQUE INDEX IF NOT EXISTS uq_trips_user_client_trip_id
  ON trips(user_id, client_trip_id)
  WHERE NULLIF(BTRIM(client_trip_id),'') IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_trips_user_start_source_key
  ON trips(user_id, start_source_key)
  WHERE NULLIF(BTRIM(start_source_key),'') IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_trips_user_finish_source_key
  ON trips(user_id, finish_source_key)
  WHERE NULLIF(BTRIM(finish_source_key),'') IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_trips_client_trip_lookup
  ON trips(user_id, client_trip_id, status);
