using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace TransPoli;

/// <summary>
/// Cria e versiona o banco local sem tocar na interface.
/// A versão inicial é deliberadamente pequena para permitir migração gradual
/// dos JSON existentes sem risco de perder dados.
/// </summary>
internal sealed class DatabaseInitializer
{
    private readonly TransPoliDb _db;

    public DatabaseInitializer(TransPoliDb db) => _db = db;

    public void Initialize()
    {
        using var transaction = _db.Connection.BeginTransaction();
        Execute(transaction, "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL);");

        var version = ReadVersion(transaction);
        if (version < 1)
        {
            CreateVersion1(transaction);
            SetVersion(transaction, 1);
        }

        transaction.Commit();
    }

    private void CreateVersion1(SqliteTransaction transaction)
    {
        Execute(transaction, @"
CREATE TABLE IF NOT EXISTS player (
    id TEXT PRIMARY KEY,
    display_name TEXT NOT NULL DEFAULT '',
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS truck (
    id TEXT PRIMARY KEY,
    player_id TEXT NULL,
    brand TEXT NOT NULL DEFAULT '',
    model TEXT NOT NULL DEFAULT '',
    plate TEXT NOT NULL DEFAULT '',
    tank_capacity_l REAL NOT NULL DEFAULT 0,
    odometer_km REAL NOT NULL DEFAULT 0,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS cargo (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL DEFAULT '',
    rate_per_km REAL NOT NULL DEFAULT 0,
    source TEXT NOT NULL DEFAULT 'local',
    active INTEGER NOT NULL DEFAULT 1,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS trip (
    id TEXT PRIMARY KEY,
    server_id TEXT NULL,
    truck_id TEXT NULL,
    cargo_id TEXT NULL,
    source_city TEXT NOT NULL DEFAULT '',
    destination_city TEXT NOT NULL DEFAULT '',
    source_company TEXT NOT NULL DEFAULT '',
    destination_company TEXT NOT NULL DEFAULT '',
    cargo_name TEXT NOT NULL DEFAULT '',
    status TEXT NOT NULL DEFAULT 'created',
    started_at_utc TEXT NULL,
    finished_at_utc TEXT NULL,
    start_odometer_km REAL NOT NULL DEFAULT 0,
    end_odometer_km REAL NOT NULL DEFAULT 0,
    planned_distance_km REAL NOT NULL DEFAULT 0,
    fuel_start_l REAL NOT NULL DEFAULT 0,
    fuel_end_l REAL NOT NULL DEFAULT 0,
    fuel_consumed_l REAL NOT NULL DEFAULT 0,
    calculated_value REAL NOT NULL DEFAULT 0,
    created_at_utc TEXT NOT NULL,
    updated_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS trip_telemetry (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    trip_id TEXT NULL,
    recorded_at_utc TEXT NOT NULL,
    speed_kph REAL NOT NULL DEFAULT 0,
    rpm REAL NOT NULL DEFAULT 0,
    odometer_km REAL NOT NULL DEFAULT 0,
    fuel_l REAL NOT NULL DEFAULT 0,
    fuel_range_km REAL NOT NULL DEFAULT 0,
    latitude REAL NULL,
    longitude REAL NULL
);

CREATE TABLE IF NOT EXISTS economy_transaction (
    id TEXT PRIMARY KEY,
    trip_id TEXT NULL,
    type TEXT NOT NULL,
    description TEXT NOT NULL DEFAULT '',
    amount REAL NOT NULL DEFAULT 0,
    occurred_at_utc TEXT NOT NULL,
    created_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS refueling (
    id TEXT PRIMARY KEY,
    trip_id TEXT NULL,
    liters REAL NOT NULL DEFAULT 0,
    price_per_liter REAL NOT NULL DEFAULT 0,
    total_cost REAL NOT NULL DEFAULT 0,
    odometer_km REAL NOT NULL DEFAULT 0,
    recorded_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS maintenance (
    id TEXT PRIMARY KEY,
    truck_id TEXT NULL,
    type TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    cost REAL NOT NULL DEFAULT 0,
    odometer_km REAL NOT NULL DEFAULT 0,
    recorded_at_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS sync_queue (
    id TEXT PRIMARY KEY,
    event_type TEXT NOT NULL,
    trip_id TEXT NULL,
    payload_json TEXT NOT NULL,
    created_at_utc TEXT NOT NULL,
    attempts INTEGER NOT NULL DEFAULT 0,
    last_attempt_at_utc TEXT NULL,
    synced_at_utc TEXT NULL
);

CREATE INDEX IF NOT EXISTS idx_trip_status ON trip(status);
CREATE INDEX IF NOT EXISTS idx_trip_started ON trip(started_at_utc);
CREATE INDEX IF NOT EXISTS idx_trip_telemetry_trip ON trip_telemetry(trip_id, recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_economy_date ON economy_transaction(occurred_at_utc);
CREATE INDEX IF NOT EXISTS idx_sync_pending ON sync_queue(synced_at_utc, created_at_utc);
CREATE INDEX IF NOT EXISTS idx_refueling_date ON refueling(recorded_at_utc);
CREATE INDEX IF NOT EXISTS idx_maintenance_truck ON maintenance(truck_id, recorded_at_utc);");
    }

    private static int ReadVersion(SqliteTransaction transaction)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    private static void SetVersion(SqliteTransaction transaction, int version)
    {
        Execute(transaction, $"INSERT INTO schema_version(version) VALUES ({version});");
    }

    private static void Execute(SqliteTransaction transaction, string sql)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
