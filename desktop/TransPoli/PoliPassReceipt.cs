using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    internal void ShowPoliPassReceipt(PoliPassRecord record)
    {
        if (EnsureModalHost() == null) return;
        var paid = record.Amount > 0;
        var gold = new SolidColorBrush(Color.FromRgb(242, 190, 45));
        var dark = new SolidColorBrush(Color.FromRgb(10, 15, 19));
        var paper = new Border { Background=Brushes.White, BorderBrush=dark, BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(16), Padding=new Thickness(22), MaxWidth=820 };
        var body = new StackPanel();
        body.Children.Add(new Border { Background=dark, Padding=new Thickness(20,14,20,14), Margin=new Thickness(-22,-22,-22,18), Child=new TextBlock { Text="POLIPASS  •  TRANSPOLI", FontSize=26, FontWeight=FontWeights.ExtraBold, Foreground=gold } });
        body.Children.Add(new TextBlock { Text="COMPROVANTE OPERACIONAL DE PASSAGEM", FontSize=14, FontWeight=FontWeights.Bold, Foreground=Brushes.DimGray, Margin=new Thickness(0,2,0,4) });
        body.Children.Add(new TextBlock { Text=paid ? "✓ PAGO • PAGAMENTO CONFIRMADO" : "PAGAMENTO PENDENTE", FontSize=13, FontWeight=FontWeights.ExtraBold, Foreground=paid ? new SolidColorBrush(Color.FromRgb(20,145,92)) : gold, Margin=new Thickness(0,0,0,16) });
        body.Children.Add(PassLine("DOCUMENTO", $"PP-{record.EventId:0000000000}"));
        body.Children.Add(PassLine("DATA / HORA", record.RecordedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss")));
        body.Children.Add(PassLine("CAMINHÃO", $"{record.TruckBrand} {record.TruckModel}".Trim()));
        body.Children.Add(PassLine("PLACA", string.IsNullOrWhiteSpace(record.LicensePlate) ? "NÃO INFORMADA" : record.LicensePlate));
        body.Children.Add(PassLine("REBOQUE(S)", record.Trailers.Count > 0 ? record.Trailers.Count.ToString(CultureInfo.InvariantCulture) : "SEM REBOQUE"));
        foreach (var trailer in record.Trailers)
            body.Children.Add(PassLine($"REBOQUE {trailer.Index + 1}", $"{trailer.Brand} {trailer.Name} • PLACA {(string.IsNullOrWhiteSpace(trailer.LicensePlate)?"N/D":trailer.LicensePlate)} • {(trailer.Axles.HasValue?$"{trailer.Axles} EIXOS":"EIXOS N/D")}"));
        body.Children.Add(PassLine("EIXOS DO CONJUNTO", record.TotalAxles?.ToString(CultureInfo.InvariantCulture) ?? "NÃO CONFIRMADOS"));
        body.Children.Add(PassLine("PESO DA CARGA", $"{record.CargoMassKg/1000f:0.0} t"));
        body.Children.Add(new Border { Height=1, Background=Brushes.Black, Margin=new Thickness(0,12,0,12) });
        body.Children.Add(new TextBlock { Text=record.Amount > 0 ? $"VALOR DA PASSAGEM  {record.Amount.ToString("C2", CultureInfo.GetCultureInfo("pt-BR"))}" : "VALOR DA PASSAGEM  AGUARDANDO CONFIRMAÇÃO", FontSize=20, FontWeight=FontWeights.Bold, Foreground=Brushes.Black, HorizontalAlignment=HorizontalAlignment.Right });
        body.Children.Add(new TextBlock { Text="Evento detectado pela telemetria ETS2. Comprovante operacional TransPoli, sem validade fiscal.", FontSize=9, Foreground=Brushes.DimGray, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,18,0,0) });
        paper.Child=body;
        ShowModalContent("polipass-receipt", BuildModalCard("POLIPASS • COMPROVANTE", paper, "Registro operacional arquivado da passagem"));
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
    public decimal SourceAmount { get; set; }
    public string SourceCurrency { get; set; } = "";
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
