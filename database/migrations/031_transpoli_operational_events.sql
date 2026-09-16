-- TransPoli operational event ingestion.
-- Stores client-generated operational events with idempotency.

CREATE TABLE IF NOT EXISTS transpoli_operational_events (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  trip_id UUID REFERENCES trips(id) ON DELETE SET NULL,
  client_event_id VARCHAR(80) NOT NULL,
  event_type VARCHAR(80) NOT NULL,
  occurred_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  payload JSONB NOT NULL DEFAULT '{}'::jsonb,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT transpoli_events_type_valid CHECK (event_type ~ '^[a-z0-9._-]{1,80}$'),
  CONSTRAINT transpoli_events_payload_object CHECK (jsonb_typeof(payload) = 'object')
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_transpoli_events_user_client
  ON transpoli_operational_events(user_id, client_event_id);

CREATE INDEX IF NOT EXISTS idx_transpoli_events_user_time
  ON transpoli_operational_events(user_id, occurred_at DESC);

CREATE INDEX IF NOT EXISTS idx_transpoli_events_trip_time
  ON transpoli_operational_events(trip_id, occurred_at DESC)
  WHERE trip_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_transpoli_events_type_time
  ON transpoli_operational_events(user_id, event_type, occurred_at DESC);
