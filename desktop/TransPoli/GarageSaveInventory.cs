using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public sealed class Ets2SaveTrailerInfo
{
    public string ProfileName { get; init; } = "";
    public string Brand { get; init; } = "";
    public string Model { get; init; } = "";
    public string Plate { get; init; } = "";
    public string Name { get; init; } = "";
    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : $"{Brand} {Model}".Trim();
}

public static class Ets2SaveTrailerScanner
{
    public static List<Ets2SaveTrailerInfo> Scan()
    {
        var result = new List<Ets2SaveTrailerInfo>();
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var roots = new[]
        {
            Path.Combine(docs, "Euro Truck Simulator 2"),
            Path.Combine(docs, "American Truck Simulator")
        };
        foreach (var root in roots)
        foreach (var saveRoot in new[] { Path.Combine(root, "profiles"), Path.Combine(root, "steam_profiles") })
        foreach (var profile in SafeDirectories(saveRoot))
        foreach (var file in SafeFiles(profile, "game.sii"))
        {
            try
            {
                var text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
                ParseBlocks(text, Path.GetFileName(profile), result);
            }
            catch { }
        }

        return result
            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
            .GroupBy(x => $"{Normalize(x.Brand)}|{Normalize(x.Model)}|{Normalize(x.Plate)}|{Normalize(x.DisplayName)}")
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void ParseBlocks(string text, string profile, List<Ets2SaveTrailerInfo> output)
    {
        var lines = text.Replace("\r", "").Split('\n');
        var depth = 0;
        var active = false;
        var buffer = new List<string>();

        foreach (var line in lines)
        {
            if (!active && Regex.IsMatch(line, @"^\s*trailer\s*:\s*", RegexOptions.IgnoreCase))
            {
                active = true;
                buffer.Clear();
                depth = Count(line, '{') - Count(line, '}');
                buffer.Add(line);
                if (depth <= 0) { Parse(buffer, profile, output); active = false; }
                continue;
            }
            if (!active) continue;
            buffer.Add(line);
            depth += Count(line, '{') - Count(line, '}');
            if (depth <= 0)
            {
                Parse(buffer, profile, output);
                active = false;
                buffer.Clear();
            }
        }
    }

    private static void Parse(List<string> block, string profile, List<Ets2SaveTrailerInfo> output)
    {
        var source = string.Join("\n", block);
        string Field(params string[] keys)
        {
            foreach (var key in keys)
            {
                var m = Regex.Match(source, "\\b" + Regex.Escape(key) + "\\s*:\\s*(?:\"(?<q>[^\"]*)\"|(?<v>[^\\s}]+))", RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["v"].Value;
            }
            return "";
        }

        var brand = Field("brand", "trailer_brand");
        var model = Field("model", "trailer_model", "name");
        var plate = Field("license_plate", "plate");
        var name = Field("trailer_name", "name", "model");
        if (string.IsNullOrWhiteSpace(brand) && string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(name)) return;
        output.Add(new Ets2SaveTrailerInfo { ProfileName = profile, Brand = brand, Model = model, Plate = plate, Name = name });
    }

    private static int Count(string text, char c) => text.Count(x => x == c);
    private static string Normalize(string value) => (value ?? "").Trim().ToLowerInvariant();
    private static IEnumerable<string> SafeDirectories(string path) { try { return Directory.EnumerateDirectories(path).ToArray(); } catch { return Array.Empty<string>(); } }
    private static IEnumerable<string> SafeFiles(string path, string name) { try { return Directory.EnumerateFiles(path, name, SearchOption.AllDirectories).Take(20).ToArray(); } catch { return Array.Empty<string>(); } }
}

public partial class MainWindow
{
    internal async Task ShowGarageSaveInventoryAsync()
    {
        if (EnsureModalHost() == null) return;
        ShowModalContent("garage-save", BuildModalLoading("CARREGANDO INVENTÁRIO DO SAVE..."));

        var scan = Ets2SaveScanner.Scan();
        var trailers = Ets2SaveTrailerScanner.Scan();
        var panel = new StackPanel();

        panel.Children.Add(ModalLine("Leitura somente do perfil/save local. O TransPoli não altera o ETS2.", 11));
        panel.Children.Add(ModalLabel("CAMINHÕES DO PERFIL / SAVE"));

        if (scan.Trucks.Count == 0)
            panel.Children.Add(ModalLine(scan.ProtectedSaveFound
                ? "O save foi encontrado, mas está protegido ou não está em formato textual legível."
                : "Nenhum caminhão legível foi encontrado neste perfil.", 12));
        else
        {
            var truckSyncToken = SecureTokenStore.Read();
            if (!string.IsNullOrWhiteSpace(truckSyncToken))
            {
                var syncTrucks = ModalButton("⟳ SINCRONIZAR TODOS OS CAMINHÕES DO SAVE");
                syncTrucks.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await SyncSaveTrucksAsync(scan.Trucks);
                };
                panel.Children.Add(syncTrucks);
                panel.Children.Add(ModalLine(
                    "A sincronização adiciona/reconcilia os caminhões legíveis do save sem apagar vínculos existentes. Se o save estiver protegido, nenhum dado é inventado.",
                    10));
            }

            foreach (var truck in scan.Trucks)
            {
                var card = new StackPanel();
                card.Children.Add(new TextBlock { Text = truck.DisplayName, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
                card.Children.Add(ModalValueRow("Placa", string.IsNullOrWhiteSpace(truck.Plate) ? "sem placa" : truck.Plate));
                card.Children.Add(ModalValueRow("Odômetro", truck.OdometerKm > 0 ? $"{truck.OdometerKm:0.0} km" : "não informado"));
                card.Children.Add(ModalValueRow("Combustível", truck.FuelLiters > 0 ? $"{truck.FuelLiters:0.0} L" : "não informado"));
                var bind = ModalButton("VINCULAR ESTE CAMINHÃO");
                bind.Click += async (_, e) => { e.Handled = true; await BindSaveTruckAsync(truck); };
                card.Children.Add(bind);
                panel.Children.Add(ModalPanel(card));
            }
        }

        panel.Children.Add(ModalLabel("REBOQUES DO PERFIL / SAVE"));
        if (trailers.Count == 0)
            panel.Children.Add(ModalLine("Nenhum reboque legível foi encontrado no save. Alguns saves/Steam Cloud podem estar protegidos; o arquivo original continua intacto.", 12));
        else
            foreach (var trailer in trailers)
            {
                var card = new StackPanel();
                card.Children.Add(new TextBlock { Text = trailer.DisplayName, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
                card.Children.Add(ModalValueRow("Marca", string.IsNullOrWhiteSpace(trailer.Brand) ? "não informada" : trailer.Brand));
                card.Children.Add(ModalValueRow("Modelo", string.IsNullOrWhiteSpace(trailer.Model) ? "não informado" : trailer.Model));
                card.Children.Add(ModalValueRow("Placa", string.IsNullOrWhiteSpace(trailer.Plate) ? "sem placa" : trailer.Plate));
                card.Children.Add(ModalValueRow("Perfil", trailer.ProfileName));
                panel.Children.Add(ModalPanel(card));
            }

        var token = SecureTokenStore.Read();
        if (!string.IsNullOrWhiteSpace(token) && trailers.Count > 0)
        {
            var sync = ModalButton("SINCRONIZAR REBOQUES COM A GARAGEM");
            sync.Click += async (_, e) =>
            {
                e.Handled = true;
                await SyncSaveTrailersAsync(trailers);
            };
            panel.Children.Add(sync);
        }

        panel.Children.Add(ModalLine("O inventário do save é somente leitura. A garagem TransPoli recebe apenas os dados identificados; nenhum arquivo do ETS2 é modificado.", 10));
        ShowModalContent("garage-save", BuildModalCard("GARAGEM TRANSPOLI • SAVE", panel, "Caminhões e reboques identificados no perfil local"));
    }

    private async Task BindSaveTruckAsync(Ets2TruckInfo truck)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) { StatusText.Text = "TransPoli • sessão não encontrada"; return; }
        try
        {
            var payload = new { brand = truck.Brand, model = truck.Model, plate = truck.Plate, label = truck.DisplayName };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/garage/bind-current");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req);
            StatusText.Text = res.IsSuccessStatusCode ? $"TransPoli • {truck.DisplayName} vinculado à garagem" : "TransPoli • não foi possível vincular o caminhão do save";
            InvalidateGarageCache();
            _lastGarageCheck = DateTime.MinValue;
            await CheckGarageAuthorizationAsync();
            await ShowGarageSaveInventoryAsync();
        }
        catch { StatusText.Text = "TransPoli • falha de comunicação com a garagem"; }
    }

    private async Task SyncSaveTrucksAsync(List<Ets2TruckInfo> trucks)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token) || trucks.Count == 0) return;

        try
        {
            var payload = new
            {
                trucks = trucks.Select(t => new
                {
                    brand = t.Brand,
                    model = t.Model,
                    plate = t.Plate,
                    label = t.DisplayName,
                    profileName = t.ProfileName
                }).ToArray()
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/garage/sync-save");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();

            if (res.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(body);
                    var created = doc.RootElement.TryGetProperty("created", out var c) ? c.GetInt32() : 0;
                    var existing = doc.RootElement.TryGetProperty("alreadyBound", out var a) ? a.GetInt32() : 0;
                    var skipped = doc.RootElement.TryGetProperty("skipped", out var s) ? s.GetInt32() : 0;
                    StatusText.Text = $"TransPoli • save sincronizado • {created} novo(s), {existing} já vinculado(s), {skipped} ignorado(s)";
                }
                catch
                {
                    StatusText.Text = $"TransPoli • {trucks.Count} caminhão(ões) enviados para sincronização";
                }

                InvalidateGarageCache();
                _lastGarageCheck = DateTime.MinValue;
                await CheckGarageAuthorizationAsync();
            }
            else
            {
                StatusText.Text = "TransPoli • não foi possível sincronizar os caminhões do save";
            }

            await ShowGarageSaveInventoryAsync();
        }
        catch
        {
            StatusText.Text = "TransPoli • falha de comunicação ao sincronizar o save";
        }
    }

    private async Task SyncSaveTrailersAsync(List<Ets2SaveTrailerInfo> trailers)
    {
        var token = SecureTokenStore.Read();
        if (string.IsNullOrWhiteSpace(token)) { StatusText.Text = "TransPoli • sessão não encontrada"; return; }
        try
        {
            var payload = new
            {
                trailers = trailers.Select(t => new
                {
                    key = $"{Normalize(t.Brand)}|{Normalize(t.Model)}|{Normalize(t.Plate)}|{Normalize(t.DisplayName)}",
                    name = t.DisplayName,
                    brand = t.Brand,
                    model = t.Model,
                    plate = t.Plate,
                    profileName = t.ProfileName
                }).ToArray()
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/me/garage/trailers/sync");
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
            req.Headers.TryAddWithoutValidation("Cookie", $"truckhub_session={token}");
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req);
            StatusText.Text = res.IsSuccessStatusCode ? $"TransPoli • {trailers.Count} reboque(s) sincronizado(s)" : "TransPoli • não foi possível sincronizar os reboques";
            await ShowGarageSaveInventoryAsync();
        }
        catch { StatusText.Text = "TransPoli • falha ao sincronizar os reboques"; }
    }

    private static string Normalize(string value) => (value ?? "").Trim().ToLowerInvariant();
}
