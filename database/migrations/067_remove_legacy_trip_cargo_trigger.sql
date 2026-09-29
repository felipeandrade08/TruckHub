-- TransPoli — remove legacy BEFORE INSERT cargo-contract trigger.
-- The current API owns cargo discovery/contract creation explicitly. The legacy
-- trigger attempted to insert cargo_contracts.trip_id before trips.id existed,
-- which conflicts with the immediate FK cargo_contracts(trip_id) -> trips(id)
-- and can abort POST /me/trips with HTTP 500.
DROP TRIGGER IF EXISTS trg_trips_discover_cargo ON trips;

-- Keep truckhub_discover_trip_cargo() installed for rollback/forensics, but it
-- must no longer execute automatically on trip INSERT.
