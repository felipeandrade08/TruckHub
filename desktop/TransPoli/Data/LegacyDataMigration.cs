using System;
using System.IO;
using System.Text.Json;

namespace TransPoli;

internal static class LegacyDataMigration
{
    public static void Prepare(LocalDataStore store)
    {
        // O estado legado global não possui users.id confiável. Ele permanece no
        // disco apenas como quarentena e nunca é importado para a conta autenticada.
        WriteMarker(store);
    }

    private static void WriteMarker(LocalDataStore store)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
        Directory.CreateDirectory(folder);
        var marker = Path.Combine(folder, "transpoli-db-migration.json");
        var state = new
        {
            updatedAtUtc = DateTime.UtcNow,
            databaseVersion = 2,
            mode = "legacy_global_files_quarantined",
            databasePath = store.DatabasePath
        };
        File.WriteAllText(marker, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }
}
