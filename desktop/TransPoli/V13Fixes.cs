using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TransPoli;

/// <summary>
/// Leitura somente do perfil/save local do ETS2/ATS para inventário da garagem.
/// Não altera arquivos do jogo.
/// </summary>
public sealed class Ets2TruckInfo{public string ProfileName{get;init;}="";public string Brand{get;init;}="";public string Model{get;init;}="";public string Plate{get;init;}="";public float OdometerKm{get;init;}public float FuelLiters{get;init;}public string DisplayName=>string.IsNullOrWhiteSpace(Brand)&&string.IsNullOrWhiteSpace(Model)?"Caminhão":$"{Brand} {Model}".Trim();}
public sealed class Ets2SaveScanResult{public List<Ets2TruckInfo> Trucks{get;}=new();public bool ProtectedSaveFound{get;set;}public string Description{get;set;}="";}
public static class Ets2SaveScanner
{
    public static Ets2SaveScanResult Scan()
    {
        var result = new Ets2SaveScanResult { Description = "Leitura somente. Nenhum arquivo do jogo será alterado." };
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var roots = new[]
        {
            Path.Combine(docs, "Euro Truck Simulator 2"),
            Path.Combine(docs, "American Truck Simulator")
        };

        var protectedFound = false;
        var candidates = new List<(string File, string Profile)>();

        foreach (var root in roots)
        {
            foreach (var saveRoot in new[]
                     {
                         Path.Combine(root, "profiles"),
                         Path.Combine(root, "steam_profiles")
                     })
            {
                if (!Directory.Exists(saveRoot)) continue;

                foreach (var profile in SafeDirectories(saveRoot))
                foreach (var file in SafeFiles(profile, "game.sii"))
                    candidates.Add((file, Path.GetFileName(profile)));
            }
        }

        foreach (var candidate in candidates.OrderByDescending(x => SafeLastWrite(x.File)))
        {
            try
            {
                var bytes = File.ReadAllBytes(candidate.File);
                if (bytes.Length == 0) continue;

                var signature = DetectSignature(bytes);
                if (signature is not null && !string.Equals(signature, "SiiN", StringComparison.OrdinalIgnoreCase))
                {
                    protectedFound = true;
                    continue;
                }

                var text = Encoding.UTF8.GetString(bytes);
                if (!text.Contains("SiiN", StringComparison.OrdinalIgnoreCase) &&
                    !text.Contains("truck", StringComparison.OrdinalIgnoreCase))
                {
                    protectedFound = true;
                    continue;
                }

                ParseTruckBlocks(text, candidate.Profile, result.Trucks);
            }
            catch
            {
                protectedFound = true;
            }
        }

        var unique = result.Trucks
            .Where(x => !string.IsNullOrWhiteSpace(x.Brand) || !string.IsNullOrWhiteSpace(x.Model) || !string.IsNullOrWhiteSpace(x.Plate))
            .GroupBy(x => $"{Normalize(x.Brand)}|{Normalize(x.Model)}|{Normalize(x.Plate)}")
            .Select(g => g.First())
            .ToList();

        result.Trucks.Clear();
        result.Trucks.AddRange(unique);
        result.ProtectedSaveFound = protectedFound;

        if (result.Trucks.Count > 0)
        {
            result.Description = $"{result.Trucks.Count} caminhão(ões) legível(is) encontrado(s) no perfil/save local. Leitura somente.";
        }
        else if (protectedFound)
        {
            result.Description = "O save foi encontrado, mas está em formato binário/criptografado ou não está em texto SII legível. O TransPoli não modifica o save.";
        }
        else
        {
            result.Description = "Nenhum game.sii foi encontrado nos perfis locais do ETS2/ATS.";
        }

        return result;
    }

    private static string? DetectSignature(byte[] bytes)
    {
        if (bytes.Length < 4) return null;
        return Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4));
    }

    private static DateTime SafeLastWrite(string file)
    {
        try { return File.GetLastWriteTimeUtc(file); }
        catch { return DateTime.MinValue; }
    }

    private static void ParseTruckBlocks(string text, string profileName, List<Ets2TruckInfo> output)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var depth = 0;
        var inTruck = false;
        var buffer = new List<string>();

        foreach (var line in lines)
        {
            if (!inTruck && Regex.IsMatch(line, @"^\s*truck\s*:\s*", RegexOptions.IgnoreCase))
            {
                inTruck = true;
                buffer.Clear();
                depth = Count(line, '{') - Count(line, '}');
                buffer.Add(line);
                if (depth <= 0)
                {
                    Parse(buffer, profileName, output);
                    inTruck = false;
                }
                continue;
            }

            if (!inTruck) continue;

            buffer.Add(line);
            depth += Count(line, '{') - Count(line, '}');

            if (depth <= 0)
            {
                Parse(buffer, profileName, output);
                inTruck = false;
                buffer.Clear();
            }
        }
    }

    private static void Parse(List<string> block, string profile, List<Ets2TruckInfo> output)
    {
        var source = string.Join("\n", block);

        string Field(string key)
        {
            var pattern = "\\b" + Regex.Escape(key) + "\\s*:\\s*(?:\"(?<q>[^\"]*)\"|(?<v>[^\\s}]+))";
            var m = Regex.Match(source, pattern, RegexOptions.IgnoreCase);
            return m.Success ? (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["v"].Value) : "";
        }

        var brand = Field("brand");
        var model = Field("model");
        var plate = Field("license_plate");
        if (string.IsNullOrWhiteSpace(brand) && string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(plate)) return;

        float Number(string key) =>
            float.TryParse(Field(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;

        output.Add(new Ets2TruckInfo
        {
            ProfileName = profile,
            Brand = brand,
            Model = model,
            Plate = plate,
            OdometerKm = Number("odometer"),
            FuelLiters = Number("fuel")
        });
    }

    private static int Count(string text, char c) => text.Count(x => x == c);
    private static string Normalize(string value) => (value ?? "").Trim().ToLowerInvariant();

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string path, string name)
    {
        try { return Directory.EnumerateFiles(path, name, SearchOption.AllDirectories).Take(100).ToArray(); }
        catch { return Array.Empty<string>(); }
    }
}

internal static class V13ModuleBootstrap{[ModuleInitializer]internal static void Initialize(){EventManager.RegisterClassHandler(typeof(MainWindow),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is MainWindow main)main.StartV13Fixes();}));EventManager.RegisterClassHandler(typeof(Button),UIElement.PreviewMouseLeftButtonDownEvent,new MouseButtonEventHandler((sender,e)=>{if(e.OriginalSource is not Button button)return;if(Window.GetWindow(button) is not MainWindow main)return;var tag=button.Tag?.ToString()??"";if(button.Tag!=null&&!tag.Equals("feature-garage",StringComparison.OrdinalIgnoreCase))return;var text=button.Content?.ToString()??"";if(tag.Equals("feature-garage",StringComparison.OrdinalIgnoreCase)||text.Contains("GARAGEM",StringComparison.OrdinalIgnoreCase)){e.Handled=true;_=main.ShowGarageSaveInventoryAsync();}else if(button.Tag==null&&(text.Contains("ABASTECIMENTO",StringComparison.OrdinalIgnoreCase)||text.Contains("COMBUSTÍVEL",StringComparison.OrdinalIgnoreCase))){e.Handled=true;main.ShowFuelPaymentModalV13();}}),true);}}


