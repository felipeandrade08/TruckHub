using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    internal void ShowPoliPassReceipt(PoliPassRecord record)
    {
        if (EnsureModalHost() == null) return;
        var gold=new SolidColorBrush(Color.FromRgb(242,190,45));
        var dark=new SolidColorBrush(Color.FromRgb(7,11,15));
        var panel=new SolidColorBrush(Color.FromRgb(13,19,25));
        var muted=new SolidColorBrush(Color.FromRgb(148,157,168));
        var green=new SolidColorBrush(Color.FromRgb(78,229,155));
        var local=record.RecordedAtUtc.ToLocalTime();
        var truck=$"{record.TruckBrand} {record.TruckModel}".Trim();
        if(string.IsNullOrWhiteSpace(truck)) truck="VEÍCULO NÃO INFORMADO";

        TextBlock Text(string value,double size,Brush color,FontWeight? weight=null)=>new()
        { Text=value,FontSize=size,Foreground=color,FontWeight=weight??FontWeights.Normal,TextWrapping=TextWrapping.Wrap };
        UIElement Detail(string label,string value)
        {
            var g=new Grid{Margin=new Thickness(0,5,0,5)};
            g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(Text(label,10,muted,FontWeights.Bold));
            var v=Text(value,13,Brushes.White,FontWeights.SemiBold);Grid.SetColumn(v,1);g.Children.Add(v);return g;
        }

        var receipt=new Border{Background=dark,BorderBrush=gold,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(18),MaxWidth=1040};
        var root=new StackPanel();

        var head=new Grid{Background=new SolidColorBrush(Color.FromRgb(10,15,20)),Margin=new Thickness(1),Height=132};
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(390)});
        var brand=new StackPanel{Margin=new Thickness(30,22,10,16)};
        brand.Children.Add(Text("POLIPASS",38,gold,FontWeights.ExtraBold));
        brand.Children.Add(Text("TRANSPOLI  •  PEDÁGIO INTELIGENTE",11,Brushes.White,FontWeights.Bold));
        head.Children.Add(brand);
        var status=new StackPanel{Margin=new Thickness(10,24,30,14),HorizontalAlignment=HorizontalAlignment.Right};
        status.Children.Add(Text("COMPROVANTE DE PASSAGEM",12,muted,FontWeights.Bold));
        status.Children.Add(Text($"PP-{record.EventId:0000000000}",23,Brushes.White,FontWeights.ExtraBold));
        status.Children.Add(Text("✓ PAGAMENTO CONFIRMADO",11,green,FontWeights.Bold));
        Grid.SetColumn(status,1);head.Children.Add(status);root.Children.Add(head);

        var content=new Grid{Margin=new Thickness(30,22,30,20)};
        content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        content.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(330)});
        var info=new StackPanel{Margin=new Thickness(0,0,28,0)};
        info.Children.Add(Text("PASSAGEM POLIPASS",22,Brushes.White,FontWeights.ExtraBold));
        info.Children.Add(Text("Registro operacional confirmado pelo Banco TransPoli",11,muted,FontWeights.Normal));
        info.Children.Add(new Border{Height=1,Background=new SolidColorBrush(Color.FromRgb(47,57,67)),Margin=new Thickness(0,14,0,10)});
        info.Children.Add(Detail("DATA / HORA",local.ToString("dd/MM/yyyy HH:mm:ss")));
        info.Children.Add(Detail("CAMINHÃO",truck));
        info.Children.Add(Detail("PLACA",string.IsNullOrWhiteSpace(record.LicensePlate)?"NÃO INFORMADA":record.LicensePlate));
        info.Children.Add(Detail("EIXOS DO CONJUNTO",record.TotalAxles.HasValue?$"{record.TotalAxles.Value} eixos":"NÃO CONFIRMADOS"));
        if(record.CargoMassKg>0) info.Children.Add(Detail("PESO DA CARGA",$"{record.CargoMassKg/1000f:0.0} t"));
        if(record.Trailers.Count>0)
        {
            info.Children.Add(Detail("REBOQUES",$"{record.Trailers.Count} acoplado(s)"));
            foreach(var trailer in record.Trailers)
            {
                var trailerName=$"{trailer.Brand} {trailer.Name}".Trim();
                var trailerParts=new[]{string.IsNullOrWhiteSpace(trailerName)?null:trailerName,string.IsNullOrWhiteSpace(trailer.LicensePlate)?null:$"PLACA {trailer.LicensePlate}",trailer.Axles.HasValue?$"{trailer.Axles.Value} EIXOS":null}.Where(x=>x!=null);
                info.Children.Add(Detail($"REBOQUE {trailer.Index+1}",string.Join(" • ",trailerParts!)));
            }
        }
        content.Children.Add(info);

        var payment=new Border{Background=panel,BorderBrush=new SolidColorBrush(Color.FromRgb(62,72,82)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(15),Padding=new Thickness(22),VerticalAlignment=VerticalAlignment.Top};
        var pay=new StackPanel();
        pay.Children.Add(Text("VALOR DA PASSAGEM",11,muted,FontWeights.Bold));
        pay.Children.Add(Text(record.Amount.ToString("C2",CultureInfo.GetCultureInfo("pt-BR")),35,gold,FontWeights.ExtraBold));
        pay.Children.Add(new Border{Height=1,Background=new SolidColorBrush(Color.FromRgb(47,57,67)),Margin=new Thickness(0,14,0,14)});
        pay.Children.Add(Text("STATUS",9,muted,FontWeights.Bold));
        pay.Children.Add(Text("PAGO",18,green,FontWeights.ExtraBold));
        pay.Children.Add(Text("Cobrança confirmada e registrada na conta operacional TransPoli.",10,muted,FontWeights.Normal));
        payment.Child=pay;Grid.SetColumn(payment,1);content.Children.Add(payment);
        root.Children.Add(content);

        var foot=new Border{Background=new SolidColorBrush(Color.FromRgb(10,15,20)),Padding=new Thickness(30,14,30,14)};
        foot.Child=Text("POLIPASS • Evento detectado pela telemetria ETS2 • comprovante operacional TransPoli • sem validade fiscal",9,muted,FontWeights.Normal);
        root.Children.Add(foot);receipt.Child=root;

        var scroll=new ScrollViewer{Content=receipt,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        ShowModalContent("polipass-receipt",BuildModalCard("POLIPASS • COMPROVANTE",scroll,"Pagamento confirmado • Banco TransPoli"));
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
    public float OdometerKm { get; set; }
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
