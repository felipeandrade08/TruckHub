using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    internal void ShowPoliPassReceipt(TelemetrySnapshot data, decimal amount, RoadCombinationSnapshot combination, long eventId)
    {
        if (EnsureModalHost() == null) return;
        var paper = new Border { Background=Brushes.White, BorderBrush=Brushes.Black, BorderThickness=new Thickness(1), Padding=new Thickness(22), MaxWidth=720 };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text="TRANSPOLI • POLIPASS", FontSize=22, FontWeight=FontWeights.Bold, Foreground=Brushes.Black });
        body.Children.Add(new TextBlock { Text="COMPROVANTE OPERACIONAL DE PASSAGEM", FontSize=11, FontWeight=FontWeights.Bold, Foreground=Brushes.DimGray, Margin=new Thickness(0,2,0,16) });
        body.Children.Add(PassLine("DOCUMENTO", $"PP-{eventId:0000000000}"));
        body.Children.Add(PassLine("DATA / HORA", DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")));
        body.Children.Add(PassLine("CAMINHÃO", $"{data.TruckBrand} {data.TruckModel}".Trim()));
        body.Children.Add(PassLine("PLACA", string.IsNullOrWhiteSpace(data.LicensePlate) ? "NÃO INFORMADA" : data.LicensePlate));
        body.Children.Add(PassLine("REBOQUE(S)", combination.HasTrailer ? combination.Trailers.Count.ToString(CultureInfo.InvariantCulture) : "SEM REBOQUE"));
        foreach (var trailer in combination.Trailers)
            body.Children.Add(PassLine($"REBOQUE {trailer.Index + 1}", $"{trailer.Brand} {trailer.Name} • PLACA {(string.IsNullOrWhiteSpace(trailer.LicensePlate)?"N/D":trailer.LicensePlate)} • {(trailer.AxleCount.HasValue?$"{trailer.AxleCount} EIXOS":"EIXOS N/D")}"));
        body.Children.Add(PassLine("EIXOS DO CONJUNTO", combination.TotalAxleCount?.ToString(CultureInfo.InvariantCulture) ?? "NÃO CONFIRMADOS"));
        body.Children.Add(PassLine("PESO DA CARGA", $"{combination.CargoMassKg/1000f:0.0} t"));
        body.Children.Add(new Border { Height=1, Background=Brushes.Black, Margin=new Thickness(0,12,0,12) });
        body.Children.Add(new TextBlock { Text=$"VALOR DA PASSAGEM  {amount:C2}", FontSize=20, FontWeight=FontWeights.Bold, Foreground=Brushes.Black, HorizontalAlignment=HorizontalAlignment.Right });
        body.Children.Add(new TextBlock { Text="Evento detectado pela telemetria ETS2. Comprovante operacional TransPoli, sem validade fiscal.", FontSize=9, Foreground=Brushes.DimGray, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,18,0,0) });
        paper.Child=body;
        ShowModalContent("polipass-receipt", BuildModalCard("POLIPASS • COMPROVANTE", paper, "Registro operacional da passagem"));
    }

    private static UIElement PassLine(string label,string value)
    {
        var g=new Grid { Margin=new Thickness(0,3,0,3) };
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(180)});
        g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        g.Children.Add(new TextBlock{Text=label,FontSize=9,FontWeight=FontWeights.Bold,Foreground=Brushes.DimGray});
        var v=new TextBlock{Text=value,FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Brushes.Black,TextWrapping=TextWrapping.Wrap}; Grid.SetColumn(v,1); g.Children.Add(v); return g;
    }
}

public sealed class PoliPassRecord
{
    public long EventId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public decimal Amount { get; set; }
    public string TruckBrand { get; set; } = "";
    public string TruckModel { get; set; } = "";
    public string LicensePlate { get; set; } = "";
    public float CargoMassKg { get; set; }
    public int? TotalAxles { get; set; }
    public System.Collections.Generic.List<PoliPassTrailerRecord> Trailers { get; set; } = new();
}
public sealed class PoliPassTrailerRecord
{
    public int Index { get; set; }
    public string Brand { get; set; } = "";
    public string Name { get; set; } = "";
    public string LicensePlate { get; set; } = "";
    public int? Axles { get; set; }
}
