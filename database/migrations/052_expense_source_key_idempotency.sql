-- TransPoli — durable idempotency for physical fuel/toll events.
-- sourceKey is generated from the physical event identity by the desktop/outbox.
CREATE UNIQUE INDEX IF NOT EXISTS uq_economy_fuel_source_key
  ON economy_ledger(user_id, (metadata->>'sourceKey'))
  WHERE entry_type='fuel_payment' AND NULLIF(metadata->>'sourceKey','') IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_economy_toll_source_key
  ON economy_ledger(user_id, (metadata->>'sourceKey'))
  WHERE entry_type='toll_event' AND NULLIF(metadata->>'sourceKey','') IS NOT NULL;
