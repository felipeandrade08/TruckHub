using Microsoft.Data.Sqlite;
using System;

namespace TransPoli;

internal sealed class DatabaseInitializer
{
    private readonly TransPoliDb _db;
    public DatabaseInitializer(TransPoliDb db) => _db = db;

    public void Initialize()
    {
        using var transaction = _db.Connection.BeginTransaction();
        Execute(transaction, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");
        var version = ReadVersion(transaction);
        if (version < 1) { CreateVersion1(transaction); SetVersion(transaction, 1); version = 1; }
        if (version < 2) { CreateVersion2(transaction); SetVersion(transaction, 2); version = 2; }
        if (version < 3) { CreateVersion3(transaction); SetVersion(transaction, 3); version = 3; }
        if (version < 4) { CreateVersion4(transaction); SetVersion(transaction, 4); version = 4; }
        if (version < 5) { CreateVersion5(transaction); SetVersion(transaction, 5); version = 5; }
        if (version < 6) { CreateVersion6(transaction); SetVersion(transaction, 6); version = 6; }
        if (version < 7) { CreateVersion7(transaction); SetVersion(transaction, 7); }
        transaction.Commit();
    }

    private void CreateVersion1(SqliteTransaction transaction)
    {
        Execute(transaction, @"
CREATE TABLE IF NOT EXISTS player (id TEXT PRIMARY KEY, display_name TEXT NOT NULL DEFAULT '', created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS truck (id TEXT PRIMARY KEY, player_id TEXT NULL, brand TEXT NOT NULL DEFAULT '', model TEXT NOT NULL DEFAULT '', plate TEXT NOT NULL DEFAULT '', tank_capacity_l REAL NOT NULL DEFAULT 0, odometer_km REAL NOT NULL DEFAULT 0, created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS cargo (id TEXT PRIMARY KEY, name TEXT NOT NULL DEFAULT '', rate_per_km REAL NOT NULL DEFAULT 0, source TEXT NOT NULL DEFAULT 'local', active INTEGER NOT NULL DEFAULT 1, created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS trip (id TEXT PRIMARY KEY, server_id TEXT NULL, truck_id TEXT NULL, cargo_id TEXT NULL, source_city TEXT NOT NULL DEFAULT '', destination_city TEXT NOT NULL DEFAULT '', source_company TEXT NOT NULL DEFAULT '', destination_company TEXT NOT NULL DEFAULT '', cargo_name TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'created', started_at_utc TEXT NULL, finished_at_utc TEXT NULL, start_odometer_km REAL NOT NULL DEFAULT 0, end_odometer_km REAL NOT NULL DEFAULT 0, planned_distance_km REAL NOT NULL DEFAULT 0, fuel_start_l REAL NOT NULL DEFAULT 0, fuel_end_l REAL NOT NULL DEFAULT 0, fuel_consumed_l REAL NOT NULL DEFAULT 0, calculated_value REAL NOT NULL DEFAULT 0, created_at_utc TEXT NOT NULL, updated_at_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS trip_telemetry (id INTEGER PRIMARY KEY AUTOINCREMENT, trip_id TEXT NULL, recorded_at_utc TEXT NOT NULL, speed_kph REAL NOT NULL DEFAULT 0, rpm REAL NOT NULL DEFAULT 0, odometer_km REAL NOT NULL DEFAULT 0, fuel_l REAL NOT NULL DEFAULT 0, fuel_range_km REAL NOT NULL DEFAULT 0, latitude REAL NULL, longitude REAL NULL);
CREATE TABLE IF NOT EXISTS economy_transaction (id TEXT PRIMARY KEY, trip_id TEXT NULL, type TEXT NOT NULL, description TEXT NOT NULL DEFAULT '', amount REAL NOT NULL DEFAULT 0, occurred_at_utc TEXT NOT NULL, created_at_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS refueling (id TEXT PRIMARY KEY, trip_id TEXT NULL, liters REAL NOT NULL DEFAULT 0, price_per_liter REAL NOT NULL DEFAULT 0, total_cost REAL NOT NULL DEFAULT 0, odometer_km REAL NOT NULL DEFAULT 0, recorded_at_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS maintenance (id TEXT PRIMARY KEY, truck_id TEXT NULL, type TEXT NOT NULL DEFAULT '', description TEXT NOT NULL DEFAULT '', cost REAL NOT NULL DEFAULT 0, odometer_km REAL NOT NULL DEFAULT 0, recorded_at_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS sync_queue (id TEXT PRIMARY KEY, event_type TEXT NOT NULL, trip_id TEXT NULL, payload_json TEXT NOT NULL, created_at_utc TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0, last_attempt_at_utc TEXT NULL, synced_at_utc TEXT NULL);
CREATE INDEX IF NOT EXISTS idx_trip_status ON trip(status);
CREATE INDEX IF NOT EXISTS idx_trip_started ON trip(started_at_utc);
CREATE INDEX IF NOT EXISTS idx_trip_telemetry_trip ON trip_telemetry(trip_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_economy_date ON economy_transaction(occurred_at_utc);
CREATE INDEX IF NOT EXISTS idx_sync_pending ON sync_queue(synced_at_utc, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_refueling_date ON refueling(recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_maintenance_truck ON maintenance(truck_id, recorded_at_utc);");
    }

    private void CreateVersion2(SqliteTransaction transaction)
    {
        Execute(transaction, @"
CREATE TABLE IF NOT EXISTS operational_event (id TEXT PRIMARY KEY, event_type TEXT NOT NULL, status TEXT NOT NULL DEFAULT '', note TEXT NOT NULL DEFAULT '', reference TEXT NOT NULL DEFAULT '', cargo_key TEXT NOT NULL DEFAULT '', trip_id TEXT NULL, driver TEXT NOT NULL DEFAULT '', truck TEXT NOT NULL DEFAULT '', recorded_at_utc TEXT NOT NULL, odometer_km REAL NOT NULL DEFAULT 0, manual INTEGER NOT NULL DEFAULT 0);
CREATE TABLE IF NOT EXISTS cargo_operation (id INTEGER PRIMARY KEY CHECK (id = 1), lifecycle TEXT NOT NULL DEFAULT 'AGUARDANDO_CARGA', route TEXT NOT NULL DEFAULT '', cargo TEXT NOT NULL DEFAULT '', speed_kph REAL NOT NULL DEFAULT 0, cargo_loaded INTEGER NOT NULL DEFAULT 0, trip_active INTEGER NOT NULL DEFAULT 0, updated INTEGER NOT NULL DEFAULT 0, last_update_utc TEXT NOT NULL, last_transition_utc TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS cargo_timeline (id INTEGER PRIMARY KEY AUTOINCREMENT, recorded_at_utc TEXT NOT NULL, lifecycle TEXT NOT NULL, details TEXT NOT NULL DEFAULT '');
CREATE TABLE IF NOT EXISTS legacy_import (file_name TEXT PRIMARY KEY, imported_at_utc TEXT NOT NULL, imported_rows INTEGER NOT NULL DEFAULT 0);
CREATE INDEX IF NOT EXISTS idx_operational_event_date ON operational_event(recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_operational_event_type ON operational_event(event_type, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_cargo_timeline_date ON cargo_timeline(recorded_at_utc);");
    }

    private void CreateVersion3(SqliteTransaction transaction)
    {
        Execute(transaction, @"
ALTER TABLE refueling ADD COLUMN station TEXT NOT NULL DEFAULT '';
ALTER TABLE refueling ADD COLUMN location TEXT NOT NULL DEFAULT '';
ALTER TABLE refueling ADD COLUMN fuel_before_l REAL NOT NULL DEFAULT 0;
ALTER TABLE refueling ADD COLUMN fuel_after_l REAL NOT NULL DEFAULT 0;
ALTER TABLE refueling ADD COLUMN truck TEXT NOT NULL DEFAULT '';
ALTER TABLE refueling ADD COLUMN license_plate TEXT NOT NULL DEFAULT '';");

        Execute(transaction, "CREATE INDEX IF NOT EXISTS idx_refueling_trip ON refueling(trip_id, recorded_at_utc);");
    }

    private void CreateVersion4(SqliteTransaction transaction)
    {
        Execute(transaction, @"
ALTER TABLE trip ADD COLUMN rate_per_km REAL NOT NULL DEFAULT 0;
ALTER TABLE trip ADD COLUMN distance_km REAL NOT NULL DEFAULT 0;
ALTER TABLE trip ADD COLUMN income_gross REAL NOT NULL DEFAULT 0;
ALTER TABLE trip ADD COLUMN expense_total REAL NOT NULL DEFAULT 0;
ALTER TABLE trip ADD COLUMN net_value REAL NOT NULL DEFAULT 0;
ALTER TABLE trip ADD COLUMN finish_reason TEXT NOT NULL DEFAULT '';
CREATE INDEX IF NOT EXISTS idx_trip_finished ON trip(finished_at_utc);
CREATE INDEX IF NOT EXISTS idx_economy_trip ON economy_transaction(trip_id, occurred_at_utc);");
    }

    private void CreateVersion5(SqliteTransaction transaction)
    {
        Execute(transaction, @"
ALTER TABLE maintenance ADD COLUMN component TEXT NOT NULL DEFAULT '';
ALTER TABLE maintenance ADD COLUMN trip_id TEXT NULL;
CREATE INDEX IF NOT EXISTS idx_maintenance_date ON maintenance(recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_maintenance_trip ON maintenance(trip_id, recorded_at_utc);");
    }

    private void CreateVersion6(SqliteTransaction transaction)
    {
        Execute(transaction, @"
CREATE TABLE IF NOT EXISTS local_loan (
    id TEXT PRIMARY KEY,
    principal REAL NOT NULL,
    remaining REAL NOT NULL,
    repayment_pct REAL NOT NULL DEFAULT 20,
    installments_total INTEGER NOT NULL DEFAULT 10,
    installments_paid INTEGER NOT NULL DEFAULT 0,
    installment_min REAL NOT NULL DEFAULT 0,
    interest_monthly_pct REAL NOT NULL DEFAULT 0,
    total_payable REAL NOT NULL DEFAULT 0,
    status TEXT NOT NULL DEFAULT 'active',
    created_at_utc TEXT NOT NULL,
    paid_at_utc TEXT NULL
);
CREATE INDEX IF NOT EXISTS idx_local_loan_status ON local_loan(status);");
    }

    private void CreateVersion7(SqliteTransaction transaction)
    {
        Execute(transaction, @"
CREATE TABLE IF NOT EXISTS driver_note (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL DEFAULT 'Nota',
    content TEXT NOT NULL DEFAULT '',
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_driver_note_updated ON driver_note(updated_at_utc DESC);");
    }

    private static int ReadVersion(SqliteTransaction transaction)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static void SetVersion(SqliteTransaction transaction, int version) => Execute(transaction, $"INSERT INTO schema_version(version) VALUES ({version});");

    private static void Execute(SqliteTransaction transaction, string sql)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
