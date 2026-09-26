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

    private static string GarageTruckKey(string? brand, string? model, string? plate)
    {
        static string Part(string? value) => (value ?? "").Trim().ToLowerInvariant().Replace("  ", " ");
        static string Plate(string? value) => new string((value ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return $"{Part(brand)}|{Part(model)}|{Plate(plate)}";
    }

    private static IEnumerable<string> SafeDirectories(string path) { try { return Directory.EnumerateDirectories(path).ToArray(); } catch { return Array.Empty<string>(); } }
    private static IEnumerable<string> SafeFiles(string path, string name) { try { return Directory.EnumerateFiles(path, name, SearchOption.AllDirectories).Take(20).ToArray(); } catch { return Array.Empty<string>(); } }
}

public partial class MainWindow
{
    private static string GarageTruckKey(string? brand, string? model, string? plate)
    {
        static string Part(string? value) => (value ?? "").Trim().ToLowerInvariant().Replace("  ", " ");
        static string Plate(string? value) => new string((value ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return $"{Part(brand)}|{Part(model)}|{Plate(plate)}";
    }

    internal async Task ShowGarageSaveInventoryAsync()
    {
        if (EnsureModalHost() == null) return;
        ShowModalContent("garage-save", BuildModalLoading("LENDO FROTA PELA TELEMETRIA ETS2..."));

        var telemetry = await LoadCurrentTelemetryAsync();
        var panel = new StackPanel();
        if (telemetry is null || !telemetry.Connected)
        {
            panel.Children.Add(ModalHero("CENTRAL DE GARAGEM • FROTA", "Telemetria ETS2", "A garagem usa o conjunto que está realmente carregado no jogo. Nenhum veículo é inventado a partir de save protegido.", "ETS2 OFFLINE", "GoldBright"));
            panel.Children.Add(ModalStatusStrip("● AGUARDANDO TELEMETRIA • ABRA O PERFIL E ENTRE NO CAMINHÃO", "Yellow"));
            panel.Children.Add(ModalLine("Quando o Connector receber a telemetria, o caminhão e os reboques acoplados aparecerão aqui automaticamente.", 12));
            ShowModalContent("garage-save", BuildModalCard("GARAGEM TRANSPOLI • TELEMETRIA", panel, "Frota operacional lida diretamente do ETS2"));
            return;
        }

        var combination = RoadCombinationTelemetry.Build(telemetry);
        _lastGarageCheck = DateTime.MinValue;
        await CheckGarageAuthorizationAsync();

        panel.Children.Add(ModalHero("CENTRAL DE GARAGEM • FROTA", "Conjunto rodoviário em uso", "Fonte operacional: telemetria real do ETS2. A garagem acompanha o caminhão e os reboques efetivamente acoplados.", combination.HasTrailer ? $"1 CAMINHÃO • {combination.Trailers.Count} REBOQUE(S)" : "1 CAMINHÃO • SEM REBOQUE", "GoldBright"));
        panel.Children.Add(ModalStatusStrip("✓ TELEMETRIA CONECTADA • FROTA OPERACIONAL ATUALIZADA", "Green"));
        panel.Children.Add(ModalSectionTitle("CAMINHÃO EM USO", "TELEMETRIA ETS2"));

        var truck = new StackPanel();
        truck.Children.Add(new TextBlock { Text = $"{telemetry.TruckBrand} {telemetry.TruckModel}".Trim(), FontSize = 18, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
        truck.Children.Add(ModalValueRow("Placa", string.IsNullOrWhiteSpace(telemetry.LicensePlate) ? "não informada" : telemetry.LicensePlate!));
        truck.Children.Add(ModalValueRow("Odômetro", telemetry.OdometerKm > 0 ? $"{telemetry.OdometerKm:0.0} km" : "não informado"));
        truck.Children.Add(ModalValueRow("Combustível", telemetry.FuelLiters >= 0 ? $"{telemetry.FuelLiters:0.0} L" : "não informado"));
        truck.Children.Add(ModalValueRow("Eixos detectados", combination.TruckAxleCount?.ToString(CultureInfo.InvariantCulture) ?? "não confirmados"));
        truck.Children.Add(ModalValueRow("Fonte", "TELEMETRIA REAL DO ETS2"));
        var alreadyBound = !string.IsNullOrWhiteSpace(_garageTruckKey) &&
            GarageTruckKey(telemetry.TruckBrand, telemetry.TruckModel, telemetry.LicensePlate) == _garageTruckKey;
        var bind = ModalButton(alreadyBound ? "✓ CAMINHÃO JÁ VINCULADO" : "🔗 VINCULAR CAMINHÃO ATUAL");
        bind.IsEnabled = !alreadyBound;
        bind.Opacity = alreadyBound ? 0.55 : 1.0;
        bind.Click += async (_, e) => { e.Handled = true; await BindCurrentTruckAsync(telemetry); };
        truck.Children.Add(bind);
        panel.Children.Add(ModalPanel(truck));

        panel.Children.Add(ModalSectionTitle("REBOQUES ACOPLADOS", "TELEMETRIA ETS2"));
        if (!combination.HasTrailer)
        {
            panel.Children.Add(ModalLine("Nenhum reboque acoplado foi informado pela telemetria neste momento.", 12));
        }
        else
        {
            foreach (var trailer in combination.Trailers)
            {
                var card = new StackPanel();
                var display = string.Join(" ", new[] { trailer.Brand, trailer.Name }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
                card.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(display) ? $"Reboque {trailer.Index + 1}" : display, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = FindResource("Text") as Brush });
                card.Children.Add(ModalValueRow("Placa", string.IsNullOrWhiteSpace(trailer.LicensePlate) ? "não informada" : trailer.LicensePlate));
                card.Children.Add(ModalValueRow("Rodas", trailer.WheelCount.ToString(CultureInfo.InvariantCulture)));
                card.Children.Add(ModalValueRow("Eixos detectados", trailer.AxleCount?.ToString(CultureInfo.InvariantCulture) ?? "não confirmados"));
                card.Children.Add(ModalValueRow("Rodas no solo", trailer.GroundedWheelCount.ToString(CultureInfo.InvariantCulture)));
                panel.Children.Add(ModalPanel(card));
            }

            var token = SecureTokenStore.Read();
            if (!string.IsNullOrWhiteSpace(token))
            {
                var telemetryTrailers = combination.Trailers.Select(t => new Ets2SaveTrailerInfo
                {
                    ProfileName = "TELEMETRIA ETS2",
                    Brand = t.Brand,
                    Model = t.Name,
                    Plate = t.LicensePlate,
                    Name = string.IsNullOrWhiteSpace(t.Name) ? t.BodyType : t.Name
                }).ToList();
                var sync = ModalButton("SINCRONIZAR REBOQUE(S) ATUAIS COM A GARAGEM");
                sync.Click += async (_, e) => { e.Handled = true; await SyncSaveTrailersAsync(telemetryTrailers); };
                panel.Children.Add(sync);
            }
        }

        panel.Children.Add(ModalLine("A Central de Garagem não depende mais da leitura textual do game.sii para a frota operacional. Ela mostra somente o conjunto confirmado pela telemetria do ETS2.", 10));
        ShowModalContent("garage-save", BuildModalCard("GARAGEM TRANSPOLI • TELEMETRIA", panel, "Caminhão e reboques do conjunto rodoviário atual"));
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
