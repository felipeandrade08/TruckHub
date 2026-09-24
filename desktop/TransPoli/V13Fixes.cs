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

public partial class MainWindow
{
    internal void ShowFuelPaymentModalV13(){var telemetry=_pendingRefuelTelemetry;var liters=_pendingRefuelLiters;if(telemetry is null||liters<=0){ShowFuelManualModalV13();return;}var panel=new StackPanel();panel.Children.Add(ModalPanel(new TextBlock{Text=$"⛽ ABASTECIMENTO DETECTADO\n{liters:0.0} litros adicionados ao tanque. Informe o valor pago, cidade e posto. O débito só será lançado depois de confirmar.",FontSize=13,FontWeight=FontWeights.Bold,Foreground=FindResource("Text") as Brush,TextWrapping=TextWrapping.Wrap}));var price=NewV13TextBox("Preço por litro (R$)");var station=NewV13TextBox("Nome do posto");var city=NewV13TextBox("Cidade");panel.Children.Add(ModalLabel("VALOR POR LITRO"));panel.Children.Add(price);panel.Children.Add(ModalLabel("POSTO"));panel.Children.Add(station);panel.Children.Add(ModalLabel("CIDADE"));panel.Children.Add(city);panel.Children.Add(ModalLine($"Total: {liters:0.0} L × preço informado.",12));var save=ModalButton("✓ CONFIRMAR ABASTECIMENTO E DESCONTAR DO BANCO");save.Click+=async(_,e)=>{e.Handled=true;if(!TryMoney(price.Text,out var priceValue)||priceValue<=0||string.IsNullOrWhiteSpace(station.Text)||string.IsNullOrWhiteSpace(city.Text)){StatusText.Text="TransPoli • informe preço, posto e cidade para concluir o abastecimento";return;}await RegisterFuelPaymentV13Async(telemetry,liters,priceValue,station.Text.Trim(),city.Text.Trim());};panel.Children.Add(save);var cancel=ModalButton("✕ CANCELAR");cancel.Click+=(_,e)=>{e.Handled=true;_pendingRefuelTelemetry=null;_pendingRefuelLiters=0;CloseOperationalModal();};panel.Children.Add(cancel);ShowModalContent("fuel-v13",BuildModalCard("⛽ ABASTECIMENTO",panel,"Pagamento manual após a detecção da telemetria"));}

    private void ShowFuelManualModalV13(){var panel=new StackPanel();panel.Children.Add(ModalLine("O valor será debitado do Banco do Motorista somente após a confirmação.",12));var liters=NewV13TextBox("Litros");var price=NewV13TextBox("Preço por litro (R$)");var station=NewV13TextBox("Nome do posto");var city=NewV13TextBox("Cidade");panel.Children.Add(ModalLabel("LITROS"));panel.Children.Add(liters);panel.Children.Add(ModalLabel("PREÇO POR LITRO"));panel.Children.Add(price);panel.Children.Add(ModalLabel("POSTO"));panel.Children.Add(station);panel.Children.Add(ModalLabel("CIDADE"));panel.Children.Add(city);var save=ModalButton("✓ CONFIRMAR E DESCONTAR DO BANCO");save.Click+=async(_,e)=>{e.Handled=true;if(!float.TryParse(liters.Text.Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out var l)||l<=0||!TryMoney(price.Text,out var p)||p<=0||string.IsNullOrWhiteSpace(station.Text)||string.IsNullOrWhiteSpace(city.Text)){StatusText.Text="TransPoli • informe litros, preço, posto e cidade";return;}var data=await LoadCurrentTelemetryAsync();if(data is null){StatusText.Text="TransPoli • telemetria indisponível";return;}await RegisterFuelPaymentV13Async(data,l,p,station.Text.Trim(),city.Text.Trim());};panel.Children.Add(save);ShowModalContent("fuel-v13",BuildModalCard("⛽ ABASTECIMENTO",panel,"Lançamento manual"));}

    private bool _refuelRegistrationBusy;
    private async Task RegisterFuelPaymentV13Async(TelemetrySnapshot data,float liters,decimal price,string station,string city)
        {
            if (_refuelRegistrationBusy || _pendingRefuelTelemetry is null || _pendingRefuelLiters <= 0) return;
            _refuelRegistrationBusy = true;
            var amount=Math.Round((decimal)liters*price,2);
            var now=DateTime.UtcNow;
            var localTripId=GetLocalTripIdForExpense();
            var refuelId = BuildDeterministicRefuelId(localTripId, _serverTripId, data.OdometerKm, liters, station, now);
            try
            {
                if(LocalData.Current is { } store)
                {
                    _refuelings.Add(new RefuelingRecord
                    {
                        Id=refuelId,RecordedAtUtc=now,Station=station,Location=city,Liters=liters,
                        FuelBefore=_fuelBefore,FuelAfter=_fuelAfter,OdometerKm=data.OdometerKm,
                        Truck=$"{data.TruckBrand} {data.TruckModel}".Trim(),LicensePlate=data.LicensePlate??"",
                        TripId=localTripId,SessionKey=_tripLifecycle.Current.SessionKey,TruckId=CanonicalTruckIdentity(data)
                    });
                    SaveOperations();
                    new LocalEconomyRepository(store.Db).AddExpense(
                        "fuel-"+refuelId,localTripId,"fuel_expense",
                        $"Abastecimento • {station} • {liters:0.0} L",
                        amount,now);
                    RefreshActiveTripFinancials(force: true);
                }
    
                var token=SecureTokenStore.Read();
                var payload=new
                {
                    liters,pricePerLiter=price,amount,station,city,odometerKm=data.OdometerKm,
                    truckBrand=data.TruckBrand,truckModel=data.TruckModel,licensePlate=data.LicensePlate,
                    tripId=_serverTripId,localTripId,sourceKey=refuelId
                };
    
                if(string.IsNullOrWhiteSpace(token))
                {
                    _serverSync.QueueExpense(_serverTripId,payload);
                    _pendingRefuelTelemetry=null;_pendingRefuelLiters=0;
                    StatusText.Text=$"TransPoli • abastecimento salvo localmente • R$ {amount:0.00} debitado";
                    CloseOperationalModal();
                    return;
                }
    
                using var request=new HttpRequestMessage(HttpMethod.Post,$"{ApiBaseUrl}/me/expenses/fuel-payment");
                request.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");
                request.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");
                request.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
                using var response=await _http.SendAsync(request);
                var text=await response.Content.ReadAsStringAsync();
    
                if(!response.IsSuccessStatusCode)
                    _serverSync.QueueExpense(_serverTripId,payload);
    
                _pendingRefuelTelemetry=null;_pendingRefuelLiters=0;
                StatusText.Text=response.IsSuccessStatusCode
                    ? $"TransPoli • abastecimento confirmado • R$ {amount:0.00} debitado do banco"
                    : $"TransPoli • abastecimento salvo localmente • R$ {amount:0.00} • sincronização pendente";
                CloseOperationalModal();
            }
            catch
            {
                try
                {
                    _serverSync.QueueExpense(_serverTripId,new
                    {
                        liters,pricePerLiter=price,amount,station,city,odometerKm=data.OdometerKm,
                        truckBrand=data.TruckBrand,truckModel=data.TruckModel,licensePlate=data.LicensePlate,
                        tripId=_serverTripId,localTripId,sourceKey=refuelId
                    });
                }
                catch { }
                StatusText.Text=$"TransPoli • abastecimento salvo localmente • R$ {amount:0.00} • sincronização pendente";
                _pendingRefuelTelemetry=null;_pendingRefuelLiters=0;
                CloseOperationalModal();
            }
            finally { _refuelRegistrationBusy = false; }
        }

    private static string BuildDeterministicRefuelId(string? localTripId, string? serverTripId, float odometerKm, float liters, string station, DateTime occurredAtUtc)
        {
            var tripKey = !string.IsNullOrWhiteSpace(localTripId) ? localTripId : serverTripId ?? "sem-viagem";
            var timeBucket = occurredAtUtc.ToUniversalTime().Ticks / TimeSpan.TicksPerMinute;
            return $"fuel-{tripKey}-{Math.Round(odometerKm, 1):0.0}-{Math.Round(liters, 1):0.0}-{timeBucket}-{station.Trim().ToLowerInvariant()}";
        }

    private string? GetLocalTripIdForExpense()
        {
            if(!string.IsNullOrWhiteSpace(_localTripId)) return _localTripId;
            return null;
        }

    private static TextBox NewV13TextBox(string placeholder)=>new(){ToolTip=placeholder,FontSize=15,Padding=new Thickness(10),Background=Application.Current.FindResource("Panel2") as Brush,Foreground=Application.Current.FindResource("Text") as Brush,BorderBrush=Application.Current.FindResource("Panel2") as Brush,Margin=new Thickness(0,0,0,2)};

    private static bool TryMoney(string text,out decimal value)=>decimal.TryParse(text.Trim().Replace('.',','),NumberStyles.Number,CultureInfo.GetCultureInfo("pt-BR"),out value)||decimal.TryParse(text.Trim().Replace(',','.'),NumberStyles.Number,CultureInfo.InvariantCulture,out value);

}

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
