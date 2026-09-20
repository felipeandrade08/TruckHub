using System;
using System.IO;
using System.Text.Json;

namespace TransPoli;

/// <summary>
/// Registro da migração inicial. Nesta fase não move nem apaga arquivos legados.
/// Apenas garante que a presença deles seja conhecida, permitindo uma migração
/// posterior por domínio sem risco de perda de histórico.
/// </summary>
internal static class LegacyDataMigration
{
    public static void Prepare()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TransPoli");

        Directory.CreateDirectory(folder);

        var marker = Path.Combine(folder, "transpoli-db-migration.json");
        if (File.Exists(marker)) return;

        var state = new
        {
            createdAtUtc = DateTime.UtcNow,
            databaseVersion = 1,
            legacyFiles = new[]
            {
                "transpoli-operations.json",
                "transpoli-cargo-operation.json",
                "transpoli-server-sync.json"
            }
        };

        File.WriteAllText(marker, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }
}
