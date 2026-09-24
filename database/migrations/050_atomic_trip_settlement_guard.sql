-- TransPoli — settlement guard kept for compatibility.
-- IMPORTANT: a transaction-scoped advisory lock is not used by the HTTP
-- settlement flow because @neondatabase/serverless may execute separate tagged
-- queries independently. Durable idempotency is enforced by database unique
-- constraints/functions instead. This helper remains available only to callers
-- that explicitly execute it inside one PostgreSQL transaction.
CREATE OR REPLACE FUNCTION lock_trip_settlement(p_trip_id UUID)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
  PERFORM pg_advisory_xact_lock(hashtextextended(p_trip_id::text, 0));
END
$$;
