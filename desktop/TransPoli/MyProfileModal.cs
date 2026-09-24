using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    internal async void ShowMyProfileModal()
    {
        var body = new StackPanel { Margin = new Thickness(4) };
        body.Children.Add(ModalHero("MOTORISTA TRANSPOLI", "Central do motorista", "Desempenho operacional, conta local, veículo atual e sincronização reunidos em um único perfil.", BuildProfileSessionText(), string.IsNullOrWhiteSpace(SecureTokenStore.Read()) ? "Yellow" : "Green"));
        var data = LastTelemetry;

        await AddEmploymentCardAsync(body);
        AddProfileHero(body, data);
        AddProfileOperational(body, data);
        AddProfileFinancial(body);
        AddProfileVehicleHealth(body, data);
        AddProfileSync(body);

        ShowModalContent(
            "my-profile",
            BuildModalCard("MEU PERFIL", body,
                "Central do motorista • desempenho local • conta operacional • funciona offline"));
    }

    private async System.Threading.Tasks.Task AddEmploymentCardAsync(Panel body)
    {
        var token=SecureTokenStore.Read();
        if(string.IsNullOrWhiteSpace(token))
        {
            body.Children.Add(ModalStatePanel("PERFIL LOCAL", "Sessão TransPoli desconectada", "O perfil operacional e os dados locais continuam disponíveis. Conecte sua sessão para consultar ou alterar o vínculo profissional.", "Yellow"));
            return;
        }
        try
        {
            using var req=new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get,$"{ApiBaseUrl}/me/company-employment");
            req.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}"); req.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");
            using var res=await _http.SendAsync(req); var json=await res.Content.ReadAsStringAsync();
            if(!res.IsSuccessStatusCode)return;
            using var doc=System.Text.Json.JsonDocument.Parse(json);
            if(!doc.RootElement.TryGetProperty("employment",out var emp)||emp.ValueKind==System.Text.Json.JsonValueKind.Null)return;
            static string P(System.Text.Json.JsonElement e,string n)=>e.TryGetProperty(n,out var v)&&v.ValueKind!=System.Text.Json.JsonValueKind.Null?v.ToString():"";
            var type=P(emp,"employment_type"); var registration=P(emp,"registration_number"); var company=P(emp,"company_name");
            var driverName=P(emp,"driver_name"); var driverEmail=P(emp,"driver_email"); var badgeIssuedAt=P(emp,"badge_issued_at");
            var aggregateShare=P(emp,"aggregate_driver_share"); var companyShare=P(emp,"company_driver_share");
            var aggregateFuel=P(emp,"aggregate_fuel_payer"); var aggregateMaintenance=P(emp,"aggregate_maintenance_payer");
            var companyFuel=P(emp,"company_driver_fuel_payer"); var companyMaintenance=P(emp,"company_driver_maintenance_payer");
            static string Payer(string value)=>value=="company"?"Empresa":"Motorista";
            static decimal Share(string value)=>decimal.TryParse(value,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var n)?Math.Clamp(n,0m,100m):0m;
            if(type=="pending"||string.IsNullOrWhiteSpace(type))
            {
                var box=new StackPanel();
                box.Children.Add(new TextBlock { Text="ESCOLHA COMO VOCÊ VAI TRABALHAR NA TRANSPOLI",FontSize=20,FontWeight=FontWeights.Bold,Foreground=FindResource("GoldBright") as Brush });
                box.Children.Add(new TextBlock { Text="Compare as regras atuais da empresa antes de confirmar. Esta escolha define divisão da receita e responsabilidade pelos custos.",FontSize=13,Foreground=FindResource("TextMuted") as Brush,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,14) });

                var choices=new UniformGrid { Columns=2,Margin=new Thickness(0,0,0,12) };
                Border ChoiceCard(string title,string share,string truck,string costs,string risk)
                {
                    var panel=new StackPanel();
                    panel.Children.Add(new TextBlock { Text=title,FontSize=18,FontWeight=FontWeights.Bold,Foreground=FindResource("TextMain") as Brush });
                    panel.Children.Add(new TextBlock { Text=$"{share}% DA PARTICIPAÇÃO DO MOTORISTA",FontSize=15,FontWeight=FontWeights.Bold,Foreground=FindResource("GoldBright") as Brush,Margin=new Thickness(0,7,0,8) });
                    panel.Children.Add(new TextBlock { Text=$"CAMINHÃO\n{truck}\n\nCUSTOS\n{costs}\n\nPERFIL\n{risk}",FontSize=13,Foreground=FindResource("TextMuted") as Brush,TextWrapping=TextWrapping.Wrap });
                    return new Border { Background=new SolidColorBrush(Color.FromRgb(12,19,25)),BorderBrush=new SolidColorBrush(Color.FromRgb(55,66,78)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(15),Margin=new Thickness(4),Child=panel };
                }
                choices.Children.Add(ChoiceCard("AGREGADO",aggregateShare,"Caminhão próprio",$"Combustível: {Payer(aggregateFuel)}\nManutenção: {Payer(aggregateMaintenance)}","Maior participação • maior responsabilidade operacional"));
                choices.Children.Add(ChoiceCard("MOTORISTA DA EMPRESA",companyShare,"Caminhão da frota TransPoli",$"Combustível: {Payer(companyFuel)}\nManutenção: {Payer(companyMaintenance)}","Menor participação • menor exposição aos custos"));
                box.Children.Add(choices);

                const decimal exampleRevenue=10000m;
                var aggregateDriver=decimal.Round(exampleRevenue*Share(aggregateShare)/100m,2);
                var companyDriver=decimal.Round(exampleRevenue*Share(companyShare)/100m,2);
                box.Children.Add(new TextBlock { Text=$"SIMULAÇÃO ILUSTRATIVA • receita TransPoli de R$ {exampleRevenue:N2}\nAgregado → motorista R$ {aggregateDriver:N2} • empresa R$ {exampleRevenue-aggregateDriver:N2} • combustível: {Payer(aggregateFuel)} • manutenção: {Payer(aggregateMaintenance)}\nMotorista da Empresa → motorista R$ {companyDriver:N2} • empresa R$ {exampleRevenue-companyDriver:N2} • combustível: {Payer(companyFuel)} • manutenção: {Payer(companyMaintenance)}",FontSize=13,Foreground=FindResource("TextMain") as Brush,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,0,4,12) });

                var warning=new TextBlock { Text="IMPORTANTE • A modalidade não pode ser alterada durante uma viagem ativa. Depois da confirmação, futuras mudanças dependem da Diretoria.",FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=FindResource("GoldBright") as Brush,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(4,0,4,12) };
                box.Children.Add(warning);
                var buttons=new UniformGrid { Columns=2 };
                var aggregate=ModalButton("CONFIRMAR COMO AGREGADO"); aggregate.Margin=new Thickness(0,0,5,0); aggregate.Click+=async(_,e)=>{e.Handled=true;await SelectEmploymentAsync("aggregate");};
                var employee=ModalButton("CONFIRMAR COMO MOTORISTA DA EMPRESA"); employee.Margin=new Thickness(5,0,0,0); employee.Click+=async(_,e)=>{e.Handled=true;await SelectEmploymentAsync("company_driver");};
                buttons.Children.Add(aggregate);buttons.Children.Add(employee);box.Children.Add(buttons);body.Children.Add(ModalPanel(box));
                return;
            }
            var label=type=="aggregate"?"AGREGADO":"MOTORISTA DA EMPRESA";
            var registrationText=string.IsNullOrWhiteSpace(registration)?"NÃO INFORMADO":registration.Trim();
            var companyText=string.IsNullOrWhiteSpace(company)?"NÃO INFORMADO":company.Trim();
            var driverNameText=string.IsNullOrWhiteSpace(driverName)?"NÃO INFORMADO":driverName.Trim();
            var driverEmailText=string.IsNullOrWhiteSpace(driverEmail)?"NÃO INFORMADO":driverEmail.Trim();
            var badgeIssuedText=DateTime.TryParse(badgeIssuedAt,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var issued)
                ? issued.ToLocalTime().ToString("dd/MM/yyyy",CultureInfo.GetCultureInfo("pt-BR"))
                : "NÃO INFORMADO";

            var badgeShell=new Border
            {
                Background=new SolidColorBrush(Color.FromRgb(8,12,16)),
                BorderBrush=FindResource("GoldBright") as Brush,
                BorderThickness=new Thickness(1),
                CornerRadius=new CornerRadius(18),
                Padding=new Thickness(18),
                Margin=new Thickness(0,8,0,12)
            };
            var badge=new Grid();
            badge.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            badge.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            badge.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });

            var header=new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            var brand=new StackPanel();
            brand.Children.Add(new TextBlock { Text="TRANSPOLI",FontSize=20,FontWeight=FontWeights.ExtraBold,Foreground=FindResource("GoldBright") as Brush });
            brand.Children.Add(new TextBlock { Text="IDENTIFICAÇÃO FUNCIONAL • MOTORISTA",FontSize=10,FontWeight=FontWeights.Bold,Foreground=FindResource("TextMuted") as Brush,Margin=new Thickness(0,2,0,0) });
            header.Children.Add(brand);
            var active=new Border { Background=new SolidColorBrush(Color.FromRgb(15,45,32)),BorderBrush=FindResource("Green") as Brush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Padding=new Thickness(9,5,9,5),VerticalAlignment=VerticalAlignment.Top };
            active.Child=new TextBlock { Text="● ATIVO",FontSize=10,FontWeight=FontWeights.Bold,Foreground=FindResource("Green") as Brush };
            Grid.SetColumn(active,1); header.Children.Add(active);
            Grid.SetRow(header,0); badge.Children.Add(header);

            var identity=new Grid { Margin=new Thickness(0,18,0,14) };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(72) });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            var portrait=new Border { Width=58,Height=72,CornerRadius=new CornerRadius(10),Background=FindResource("Panel2") as Brush,BorderBrush=FindResource("StrokeStrong") as Brush,BorderThickness=new Thickness(1),VerticalAlignment=VerticalAlignment.Top };
            portrait.Child=new TextBlock { Text="ID",FontSize=18,FontWeight=FontWeights.ExtraBold,Foreground=FindResource("GoldBright") as Brush,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center };
            identity.Children.Add(portrait);
            var info=new StackPanel();
            info.Children.Add(new TextBlock { Text=driverNameText.ToUpperInvariant(),FontSize=23,FontWeight=FontWeights.Bold,Foreground=FindResource("TextMain") as Brush,TextWrapping=TextWrapping.Wrap });
            info.Children.Add(new TextBlock { Text=label,FontSize=11,FontWeight=FontWeights.Bold,Foreground=FindResource("GoldBright") as Brush,Margin=new Thickness(0,3,0,0) });
            info.Children.Add(new TextBlock { Text=$"Registro funcional  {registrationText}",FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=FindResource("TextMain") as Brush,Margin=new Thickness(0,6,0,0) });
            info.Children.Add(new TextBlock { Text=$"E-mail  {driverEmailText}\nEmpresa  {companyText}\nEmissão do crachá  {badgeIssuedText}",FontSize=12,Foreground=FindResource("TextMuted") as Brush,Margin=new Thickness(0,3,0,0),TextWrapping=TextWrapping.Wrap });
            Grid.SetColumn(info,1); identity.Children.Add(info);
            Grid.SetRow(identity,1); badge.Children.Add(identity);

            var footer=new Border { Background=FindResource("Panel2") as Brush,BorderBrush=FindResource("Stroke") as Brush,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(11,8,11,8) };
            footer.Child=new TextBlock { Text=$"CATEGORIA  {label}   •   SITUAÇÃO  VÍNCULO ATIVO   •   REGISTRO  {registrationText}",FontSize=10,FontWeight=FontWeights.Bold,Foreground=FindResource("TextMuted") as Brush,TextWrapping=TextWrapping.Wrap };
            Grid.SetRow(footer,2); badge.Children.Add(footer);
            badgeShell.Child=badge;
            body.Children.Add(badgeShell);
        }
        catch { }
    }

    private async System.Threading.Tasks.Task SelectEmploymentAsync(string employmentType)
    {
        if(_tripActive){MessageBox.Show("Finalize a TripSession ativa antes de definir seu vínculo profissional.","TransPoli",MessageBoxButton.OK,MessageBoxImage.Information);return;}
        var name=employmentType=="aggregate"?"Agregado":"Motorista TransPoli";
        if(MessageBox.Show($"Confirmar {name}? Depois da confirmação, alterações deverão passar pela Diretoria.","TransPoli",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
        var token=SecureTokenStore.Read(); if(string.IsNullOrWhiteSpace(token))return;
        using var req=new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post,$"{ApiBaseUrl}/me/company-employment/select");
        req.Headers.TryAddWithoutValidation("Authorization",$"Bearer {token}");req.Headers.TryAddWithoutValidation("Cookie",$"truckhub_session={token}");
        req.Content=new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(new { employmentType }),System.Text.Encoding.UTF8,"application/json");
        using var res=await _http.SendAsync(req);var json=await res.Content.ReadAsStringAsync();
        if(!res.IsSuccessStatusCode){MessageBox.Show("Não foi possível confirmar a modalidade. "+json,"TransPoli",MessageBoxButton.OK,MessageBoxImage.Error);return;}
        ShowMyProfileModal();
    }

    private void AddProfileHero(Panel body, TelemetrySnapshot? data)
    {
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 12, 17, 23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(96, 74, 11)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(20, 18, 20, 18),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var row = new StackPanel();
        row.Children.Add(new TextBlock
        {
            Text = "MOTORISTA TRANSPOLI",
            Foreground = FindResource("GoldBright") as Brush,
            FontSize = 12,
            FontWeight = FontWeights.Bold
        });
        row.Children.Add(new TextBlock
        {
            Text = "Perfil operacional",
            Foreground = FindResource("TextMain") as Brush,
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 3, 0, 0)
        });
        row.Children.Add(new TextBlock
        {
            Text = BuildProfileSessionText(),
            Foreground = FindResource("TextMuted") as Brush,
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0)
        });

        var truck = data is null
            ? "Caminhão aguardando conexão"
            : $"{data.TruckBrand ?? "Caminhão"} {data.TruckModel ?? ""}".Trim();

        row.Children.Add(new TextBlock
        {
            Text = truck,
            Foreground = FindResource("TextMain") as Brush,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 0)
        });

        row.Children.Add(new TextBlock
        {
            Text = data is null
                ? "Placa não disponível"
                : $"Placa: {data.LicensePlate ?? "não informada"}  •  ID: {data.TruckId ?? "não informado"}",
            Foreground = FindResource("TextMuted") as Brush,
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0)
        });

        card.Child = row;
        body.Children.Add(card);
    }

    private void AddProfileOperational(Panel body, TelemetrySnapshot? data)
    {
        var stats = QueryProfileStats();
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };

        AddProfileMetric(grid, "VIAGENS", stats.Trips.ToString(CultureInfo.InvariantCulture));
        AddProfileMetric(grid, "DISTÂNCIA", $"{stats.DistanceKm:0.0} km");
        AddProfileMetric(grid, "COMBUSTÍVEL", $"{stats.FuelLiters:0.0} L");
        AddProfileMetric(grid, "RECEITA", $"R$ {stats.Revenue:0.00}");
        AddProfileMetric(grid, "DESPESAS", $"R$ {stats.Expenses:0.00}");
        AddProfileMetric(grid, "RESULTADO", $"R$ {stats.Net:0.00}");

        body.Children.Add(grid);

        var state = data is null || !data.Connected
            ? "OFFLINE • conecte o ETS2 para telemetria ao vivo"
            : data.GamePaused
                ? "ETS2 CONECTADO • jogo pausado"
                : "ETS2 CONECTADO • telemetria em tempo real";

        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 11, 18, 24)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(11),
            Child = new TextBlock
            {
                Text = $"●  {state}",
                Foreground = data?.Connected == true
                    ? FindResource("Green") as Brush
                    : FindResource("TextMuted") as Brush,
                FontSize = 12,
                FontWeight = FontWeights.Bold
            }
        });
    }

    private void AddProfileFinancial(Panel body)
    {
        var stats = QueryProfileStats();
        var text = new TextBlock
        {
            Text = $"Conta operacional local  •  média de receita/km: R$ {stats.RevenuePerKm:0.00}  •  custo/km: R$ {stats.CostPerKm:0.00}",
            Foreground = FindResource("TextMuted") as Brush,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(3, 10, 3, 0)
        };
        body.Children.Add(text);
    }

    private void AddProfileVehicleHealth(Panel body, TelemetrySnapshot? data)
    {
        var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
        var wear = data is null
            ? 0f
            : Math.Clamp(Math.Max(Math.Max(Math.Max(data.WearEngine, data.WearTransmission), data.WearCabin), Math.Max(data.WearChassis, data.WearWheels)) * 100f, 0f, 100f);

        var health = wear >= 75f ? "CRÍTICO" : wear >= 50f ? "ATENÇÃO" : "NORMAL";
        var healthBrush = wear >= 75f ? FindResource("Red") as Brush : wear >= 50f ? FindResource("GoldBright") as Brush : FindResource("Green") as Brush;

        AddProfileMetric(grid, "DESGASTE", $"{wear:0}%");
        AddProfileMetric(grid, "MANUTENÇÃO", data is null ? "—" : health);
        AddProfileMetric(grid, "MOTOR", data?.EngineEnabled == true ? "LIGADO" : "DESLIGADO");

        if (grid.Children.Count >= 2 && grid.Children[1] is Border maintenanceCard && maintenanceCard.Child is StackPanel stack && stack.Children.Count > 1 && stack.Children[1] is TextBlock value)
            value.Foreground = healthBrush;

        body.Children.Add(grid);

        var truckState = data is null
            ? "Caminhão não conectado"
            : string.IsNullOrWhiteSpace(data.TruckId) && string.IsNullOrWhiteSpace(data.LicensePlate)
                ? "Caminhão detectado • identificação não informada"
                : $"Caminhão vinculado • {(data.LicensePlate ?? "placa não informada")}";

        body.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(190, 12, 17, 23)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 7, 0, 0),
            Child = new TextBlock
            {
                Text = $"🚛  {truckState}  •  {BuildProfileLastTripText()}",
                Foreground = FindResource("TextMuted") as Brush,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            }
        });
    }

    private string BuildProfileLastTripText()
    {
        try
        {
            if (LocalData.Current is not { } store) return "última viagem: —";
            using var c = store.Db.Connection.CreateCommand();
            c.CommandText = @"SELECT cargo_name, source_city, destination_city, finished_at_utc FROM trip WHERE status='finished' ORDER BY finished_at_utc DESC LIMIT 1;";
            using var reader = c.ExecuteReader();
            if (!reader.Read()) return "última viagem: —";
            var cargo = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "Carga";
            var origin = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "Origem";
            var destination = Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture) ?? "Destino";
            var finished = Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture);
            return $"última viagem: {cargo} • {origin} → {destination} • {FormatProfileDate(finished)}";
        }
        catch { return "última viagem: —"; }
    }

    private static string FormatProfileDate(string? value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return "—";
        return parsed.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
    }

    private void AddProfileSync(Panel body)
    {
        var pending = 0;
        try { pending = LocalData.Current?.Db is { } db ? GetProfilePendingSync(db) : 0; } catch { }

        body.Children.Add(new TextBlock
        {
            Text = pending == 0
                ? "🔒 BANCO LOCAL • tudo sincronizado • offline disponível"
                : $"↻ BANCO LOCAL • {pending} item(ns) pendente(s) de sincronização",
            Foreground = pending == 0
                ? FindResource("Green") as Brush
                : FindResource("GoldBright") as Brush,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(3, 8, 3, 0)
        });
    }

    private void AddProfileMetric(Panel grid, string label, string value)
    {
        grid.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 16, 23, 32)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(38, 49, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Margin = new Thickness(3),
            Padding = new Thickness(10, 9, 10, 9),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, Foreground = FindResource("TextMuted") as Brush, FontSize = 12, FontWeight = FontWeights.Bold },
                    new TextBlock { Text = value, Foreground = FindResource("TextMain") as Brush, FontSize = 17, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 0) }
                }
            }
        });
    }

    private string BuildProfileSessionText()
    {
        return string.IsNullOrWhiteSpace(SecureTokenStore.Read())
            ? "Sessão central não conectada • dados locais continuam disponíveis"
            : "Sessão TransPoli ativa • sincronização central complementar";
    }

    private static int GetProfilePendingSync(TransPoliDb db)
    {
        using var c = db.Connection.CreateCommand();
        c.CommandText = "SELECT COUNT(*) FROM sync_queue WHERE synced_at_utc IS NULL;";
        return Convert.ToInt32(c.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private ProfileStats QueryProfileStats()
    {
        var result = new ProfileStats();
        try
        {
            if (LocalData.Current is not { } store) return result;

            using var c = store.Db.Connection.CreateCommand();
            c.CommandText = @"
SELECT
    COUNT(CASE WHEN status='finished' THEN 1 END),
    COALESCE(SUM(CASE WHEN status='finished' THEN distance_km ELSE 0 END),0),
    COALESCE(SUM(CASE WHEN status='finished' THEN fuel_consumed_l ELSE 0 END),0),
    COALESCE(SUM(CASE WHEN status='finished' THEN income_gross ELSE 0 END),0),
    COALESCE(SUM(CASE WHEN status='finished' THEN expense_total ELSE 0 END),0),
    COALESCE(SUM(CASE WHEN status='finished' THEN net_value ELSE 0 END),0)
FROM trip;";
            using var reader = c.ExecuteReader();
            if (!reader.Read()) return result;

            result.Trips = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            result.DistanceKm = Convert.ToDouble(reader.GetValue(1), CultureInfo.InvariantCulture);
            result.FuelLiters = Convert.ToDouble(reader.GetValue(2), CultureInfo.InvariantCulture);
            result.Revenue = Convert.ToDecimal(reader.GetValue(3), CultureInfo.InvariantCulture);
            result.Expenses = Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture);
            result.Net = Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture);
            result.RevenuePerKm = result.DistanceKm > 0 ? (double)result.Revenue / result.DistanceKm : 0;
            result.CostPerKm = result.DistanceKm > 0 ? (double)result.Expenses / result.DistanceKm : 0;
        }
        catch { }

        return result;
    }

    private sealed class ProfileStats
    {
        public int Trips;
        public double DistanceKm;
        public double FuelLiters;
        public decimal Revenue;
        public decimal Expenses;
        public decimal Net;
        public double RevenuePerKm;
        public double CostPerKm;
    }
}
