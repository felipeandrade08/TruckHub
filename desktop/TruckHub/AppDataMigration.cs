using System;
using System.IO;

namespace TruckHub;

/// <summary>
/// Traz os dados da pasta antiga (%LOCALAPPDATA%\TransPoli) para a nova (%LOCALAPPDATA%\TruckHub) na primeira
/// execução depois da troca de nome: login, banco local, estado das viagens e configurações. Só copia; nunca
/// apaga nada da pasta antiga e nunca sobrescreve um arquivo que já exista na nova.
/// </summary>
internal static class AppDataMigration
{
    private static readonly string[] SkipExt = { ".exe", ".dll", ".pdb", ".manifest", ".config", ".msi", ".tmp" };

    public static void Run()
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var oldDir = Path.Combine(root, "TransPoli");
            var newDir = Path.Combine(root, "TruckHub");
            if (!Directory.Exists(oldDir)) return;
            Directory.CreateDirectory(newDir);
            foreach (var file in Directory.GetFiles(oldDir))
            {
                var name = Path.GetFileName(file);
                var lower = name.ToLowerInvariant();
                if (Array.IndexOf(SkipExt, Path.GetExtension(lower)) >= 0) continue;
                if (lower.EndsWith(".deps.json") || lower.EndsWith(".runtimeconfig.json")) continue;
                var target = lower.StartsWith("transpoli") ? "truckhub" + name.Substring("transpoli".Length) : name;
                var dest = Path.Combine(newDir, target);
                if (File.Exists(dest)) continue;
                try { File.Copy(file, dest, overwrite: false); } catch { }
            }
        }
        catch { }
    }
}
