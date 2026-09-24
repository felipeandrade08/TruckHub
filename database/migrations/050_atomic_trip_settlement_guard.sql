-- TransPoli — serialize settlement of the same trip across retries/concurrent requests.
-- The existing unique trip_income index remains the final ledger constraint.
-- This helper acquires a transaction-scoped advisory lock from the immutable TripId.
CREATE OR REPLACE FUNCTION lock_trip_settlement(p_trip_id UUID)
RETURNS VOID
LANGUAGE plpgsql
AS $$
BEGIN
  PERFORM pg_advisory_xact_lock(hashtextextended(p_trip_id::text, 0));
END
$$;
