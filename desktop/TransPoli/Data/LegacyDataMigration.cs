using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TransPoli;

internal static class LegacyDataMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void Prepare(LocalDataStore store)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        ImportOperations(store, Path.Combine(folder, "transpoli-operations.json"));
        ImportCargo(store, Path.Combine(folder, "transpoli-cargo-operation.json"));
        ImportSync(store, Path.Combine(folder, "transpoli-server-sync.json"));
        WriteMarker(store);
    }

    private static void ImportOperations(LocalDataStore store, string path)
    {
        const string fileName = "transpoli-operations.json";
        if (!File.Exists(path) || IsImported(store, fileName)) return;
        try
        {
            var state = JsonSerializer.Deserialize<OperationsState>(File.ReadAllText(path), JsonOptions);
            if (state is null) return;
            var rows = 0;
            foreach (var item in state.Refuelings ?? new List<RefuelingRecord>()) { InsertRefueling(store.Db.Connection, item); rows++; }
            foreach (var item in state.Stops ?? new List<StopRecord>()) { InsertOperationalEvent(store.Db.Connection, item.Id, "stop", item.Type, item.Note, "", "", item.TripKey, "", "", item.StartedAtUtc, item.OdometerKm, item.Manual); rows++; }
            foreach (var item in state.Occurrences ?? new List<OccurrenceRecord>()) { InsertOperationalEvent(store.Db.Connection, item.Id, "occurrence", item.Type, item.Details, "", "", null, "", "", item.RecordedAtUtc, item.OdometerKm, true); rows++; }
            foreach (var item in state.Documents ?? new List<DocumentRecord>()) { InsertOperationalEvent(store.Db.Connection, item.Id, "document", item.Status, "", item.Reference, item.CargoKey, item.TripId, item.Driver, item.Truck, item.RecordedAtUtc, 0, false); rows++; }
            MarkImported(store.Db.Connection, fileName, rows);
        }
        catch { }
    }

    private static void ImportCargo(LocalDataStore store, string path)
    {
        const string fileName = "transpoli-cargo-operation.json";
        if (!File.Exists(path) || IsImported(store, fileName)) return;
        try
        {
            var data = JsonSerializer.Deserialize<CargoPersistence>(File.ReadAllText(path), JsonOptions);
            if (data is null) return;
            var s = data.State ?? new CargoOperationState();
            using (var command = store.Db.Connection.CreateCommand())
            {
                command.CommandText = @"INSERT INTO cargo_operation (id,lifecycle,route,cargo,speed_kph,cargo_loaded,trip_active,updated,last_update_utc,last_transition_utc)
VALUES (1,@life,@route,@cargo,@speed,@loaded,@active,@updated,@last,@transition)
ON CONFLICT(id) DO UPDATE SET lifecycle=excluded.lifecycle,route=excluded.route,cargo=excluded.cargo,speed_kph=excluded.speed_kph,cargo_loaded=excluded.cargo_loaded,trip_active=excluded.trip_active,updated=excluded.updated,last_update_utc=excluded.last_update_utc,last_transition_utc=excluded.last_transition_utc;";
                Add(command,"@life",s.Lifecycle.ToString()); Add(command,"@route",s.Route); Add(command,"@cargo",s.Cargo); Add(command,"@speed",s.SpeedKph);
                Add(command,"@loaded",s.CargoLoaded?1:0); Add(command,"@active",s.TripActive?1:0); Add(command,"@updated",s.Updated?1:0);
                Add(command,"@last",s.LastUpdateUtc.ToUniversalTime().ToString("O")); Add(command,"@transition",s.LastTransitionUtc.ToUniversalTime().ToString("O")); command.ExecuteNonQuery();
            }
            var rows = 1;
            foreach (var item in data.Timeline ?? new List<CargoTimelineEntry>())
            {
                using var command = store.Db.Connection.CreateCommand();
                command.CommandText = "INSERT INTO cargo_timeline(recorded_at_utc,lifecycle,details) VALUES (@at,@life,@details);";
                Add(command,"@at",item.AtUtc.ToUniversalTime().ToString("O")); Add(command,"@life",item.Lifecycle.ToString()); Add(command,"@details",item.Details); command.ExecuteNonQuery(); rows++;
            }
            MarkImported(store.Db.Connection,fileName,rows);
        }
        catch { }
    }

    private static void ImportSync(LocalDataStore store, string path)
    {
        const string fileName = "transpoli-server-sync.json";
        if (!File.Exists(path) || IsImported(store, fileName)) return;
        try
        {
            var data = JsonSerializer.Deserialize<List<LegacySyncEvent>>(File.ReadAllText(path), JsonOptions);
            if (data is null) return;
            var rows = 0;
            foreach (var item in data)
            {
                using var command = store.Db.Connection.CreateCommand();
                command.CommandText = @"INSERT OR IGNORE INTO sync_queue (id,event_type,trip_id,payload_json,created_at_utc,attempts,last_attempt_at_utc,synced_at_utc)
VALUES (@id,@type,@trip,@payload,@created,0,NULL,NULL);";
                Add(command,"@id",string.IsNullOrWhiteSpace(item.Id)?Guid.NewGuid().ToString("N"):item.Id); Add(command,"@type",item.Type); Add(command,"@trip",item.TripId);
                Add(command,"@payload",item.Payload.ValueKind==JsonValueKind.Undefined?"{}":item.Payload.GetRawText()); Add(command,"@created",item.CreatedAtUtc.ToUniversalTime().ToString("O")); command.ExecuteNonQuery(); rows++;
            }
            MarkImported(store.Db.Connection,fileName,rows);
        }
        catch { }
    }

    private static void InsertRefueling(SqliteConnection connection, RefuelingRecord item)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO refueling (id,trip_id,liters,price_per_liter,total_cost,odometer_km,recorded_at_utc) VALUES (@id,NULL,@liters,0,0,@odo,@at);";
        Add(command,"@id",string.IsNullOrWhiteSpace(item.Id)?Guid.NewGuid().ToString("N"):item.Id); Add(command,"@liters",item.Liters); Add(command,"@odo",item.OdometerKm); Add(command,"@at",item.RecordedAtUtc.ToUniversalTime().ToString("O")); command.ExecuteNonQuery();
    }

    private static void InsertOperationalEvent(SqliteConnection connection,string id,string eventType,string status,string note,string reference,string cargoKey,string? tripId,string driver,string truck,DateTime recordedAt,float odometer,bool manual)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO operational_event (id,event_type,status,note,reference,cargo_key,trip_id,driver,truck,recorded_at_utc,odometer_km,manual) VALUES (@id,@type,@status,@note,@reference,@cargoKey,@trip,@driver,@truck,@at,@odo,@manual);";
        Add(command,"@id",string.IsNullOrWhiteSpace(id)?Guid.NewGuid().ToString("N"):id); Add(command,"@type",eventType); Add(command,"@status",status); Add(command,"@note",note); Add(command,"@reference",reference); Add(command,"@cargoKey",cargoKey); Add(command,"@trip",tripId); Add(command,"@driver",driver); Add(command,"@truck",truck); Add(command,"@at",recordedAt.ToUniversalTime().ToString("O")); Add(command,"@odo",odometer); Add(command,"@manual",manual?1:0); command.ExecuteNonQuery();
    }

    private static bool IsImported(LocalDataStore store,string fileName)
    {
        using var command=store.Db.Connection.CreateCommand(); command.CommandText="SELECT COUNT(1) FROM legacy_import WHERE file_name=@file;"; Add(command,"@file",fileName);
        return Convert.ToInt32(command.ExecuteScalar()??0)>0;
    }

    private static void MarkImported(SqliteConnection connection,string fileName,int rows)
    {
        using var command=connection.CreateCommand(); command.CommandText="INSERT INTO legacy_import(file_name,imported_at_utc,imported_rows) VALUES (@file,@at,@rows) ON CONFLICT(file_name) DO UPDATE SET imported_at_utc=excluded.imported_at_utc,imported_rows=excluded.imported_rows;";
        Add(command,"@file",fileName); Add(command,"@at",DateTime.UtcNow.ToString("O")); Add(command,"@rows",rows); command.ExecuteNonQuery();
    }

    private static void Add(SqliteCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);

    private static void WriteMarker(LocalDataStore store)
    {
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TransPoli");
        var marker=Path.Combine(folder,"transpoli-db-migration.json");
        var state=new { updatedAtUtc=DateTime.UtcNow,databaseVersion=2,mode="imported_without_deleting_legacy_files",databasePath=store.DatabasePath };
        File.WriteAllText(marker,JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true}));
    }

    private sealed class LegacySyncEvent
    {
        public string Id { get; set; }="";
        public string Type { get; set; }="";
        public string? TripId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public JsonElement Payload { get; set; }
    }
}
