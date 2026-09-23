using System;
using System.Windows;

namespace TransPoli;

public partial class HudSettingsWindow : Window
{
    private readonly HudSettings _settings;
    private readonly Action<HudSettings> _onPreview;
    private readonly HudSettings _original;
    private bool _loading = true;
    public HudSettingsWindow(HudSettings settings, Action<HudSettings> onPreview)
    {
        InitializeComponent();
        _settings = settings;
        _original = Clone(settings);
        _onPreview = onPreview;
        LoadValues();
        _loading = false;
    }
    private void LoadValues()
    {
        EnabledCheck.IsChecked=_settings.Enabled; LayoutCombo.SelectedIndex=_settings.LayoutMode=="Minimalista"?2:_settings.LayoutMode=="Compacta"?1:0; SpeedCheck.IsChecked=_settings.ShowSpeed; RpmCheck.IsChecked=_settings.ShowRpm; RangeCheck.IsChecked=_settings.ShowRange; OdometerCheck.IsChecked=_settings.ShowOdometer; TripKmCheck.IsChecked=_settings.ShowTripKm;
        RouteCheck.IsChecked=_settings.ShowRoute; CompaniesCheck.IsChecked=_settings.ShowCompanies; ProgressCheck.IsChecked=_settings.ShowProgress; CargoCheck.IsChecked=_settings.ShowCargo;
        ProfitCheck.IsChecked=_settings.ShowProfit; ExpensesCheck.IsChecked=_settings.ShowExpenses; TripStateCheck.IsChecked=_settings.ShowTripState; EtaCheck.IsChecked=_settings.ShowEta; FuelCheck.IsChecked=_settings.ShowFuel; GearCheck.IsChecked=_settings.ShowGear; ConnectionCheck.IsChecked=_settings.ShowConnection; AlertsCheck.IsChecked=_settings.ShowAlerts;
        PositionCombo.SelectedIndex=PositionIndex(_settings); OpacitySlider.Value=_settings.Opacity; ScaleSlider.Value=_settings.Scale; XSlider.Value=_settings.CustomX; YSlider.Value=_settings.CustomY; RefreshLabels();
    }
    private void ReadValues()
    {
        _settings.Enabled=EnabledCheck.IsChecked==true; _settings.LayoutMode=(LayoutCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()??"Completa"; _settings.CompactMode=_settings.LayoutMode=="Compacta"; _settings.ShowSpeed=SpeedCheck.IsChecked==true; _settings.ShowRpm=RpmCheck.IsChecked==true; _settings.ShowRange=RangeCheck.IsChecked==true; _settings.ShowOdometer=OdometerCheck.IsChecked==true; _settings.ShowTripKm=TripKmCheck.IsChecked==true;
        _settings.ShowRoute=RouteCheck.IsChecked==true; _settings.ShowCompanies=CompaniesCheck.IsChecked==true; _settings.ShowProgress=ProgressCheck.IsChecked==true; _settings.ShowCargo=CargoCheck.IsChecked==true;
        _settings.ShowProfit=ProfitCheck.IsChecked==true; _settings.ShowExpenses=ExpensesCheck.IsChecked==true; _settings.ShowTripState=TripStateCheck.IsChecked==true; _settings.ShowEta=EtaCheck.IsChecked==true; _settings.ShowFuel=FuelCheck.IsChecked==true; _settings.ShowGear=GearCheck.IsChecked==true; _settings.ShowConnection=ConnectionCheck.IsChecked==true; _settings.ShowAlerts=AlertsCheck.IsChecked==true; _settings.CompactMode=_settings.LayoutMode=="Compacta";
        _settings.Position=(PositionCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()??"Centro superior"; _settings.UseCustomPosition=_settings.Position=="Personalizado"; _settings.CustomX=XSlider.Value; _settings.CustomY=YSlider.Value; _settings.Opacity=OpacitySlider.Value; _settings.Scale=ScaleSlider.Value;
    }
    private void SettingChanged(object sender,RoutedEventArgs e){ApplyPreview();}
    private void SettingChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e){ApplyPreview();}
    private void SettingChanged(object sender,RoutedPropertyChangedEventArgs<double> e){ApplyPreview();}
    private void ApplyPreview(){if(_loading)return; ReadValues(); RefreshLabels(); _onPreview(_settings);}
    private void RefreshLabels()
    {
        OpacityText.Text=$"{_settings.Opacity*100:0}%"; ScaleText.Text=$"{_settings.Scale*100:0}%"; XText.Text=$"{_settings.CustomX*100:0}%"; YText.Text=$"{_settings.CustomY*100:0}%";
        if(PreviewText==null)return;
        var minimal=_settings.LayoutMode=="Minimalista";
        var compact=_settings.LayoutMode=="Compacta";
        PreviewModeText.Text=minimal?"MINIMALISTA • 620 × 82":compact?"COMPACTA • 860 × 102":"COMPLETA • 1120 × 128";
        PreviewHudShell.Width=minimal?420:compact?535:Double.NaN;
        PreviewHudShell.HorizontalAlignment=minimal?HorizontalAlignment.Left:compact?HorizontalAlignment.Center:HorizontalAlignment.Stretch;
        PreviewHudShell.CornerRadius=new CornerRadius(minimal?12:compact?13:14);
        PreviewText.FontSize=minimal?16:compact?14:14;
        PreviewText.Text=minimal?"82 KM/H   •   MARCHA 8   •   420 L   •   ETA 1H24":compact?"TRANSPOLI   •   82 KM/H   •   1.350 RPM   •   MARCHA 8   •   420 L":"TRANSPOLI  •  VIAGEM ATIVA  •  82 KM/H  •  1.350 RPM  •  420 L  •  ETA 1H24";
        PreviewDetailText.Text="São Paulo → Curitiba  •  Carga em trânsito  •  progresso 64%";
        PreviewDetailText.Visibility=minimal||compact?Visibility.Collapsed:Visibility.Visible;
    }
    private static int PositionIndex(HudSettings s) => s.UseCustomPosition ? 9 : s.Position switch { "Superior esquerdo"=>0, "Topo"=>1, "Superior direito"=>2, "Centro esquerdo"=>3, "Centro"=>4, "Centro direito"=>5, "Inferior esquerdo"=>6, "Inferior"=>7, "Inferior direito"=>8, _=>1 };
    private void Save_Click(object sender,RoutedEventArgs e){ReadValues();_settings.Save();_onPreview(_settings);DialogResult=true;Close();}
    private void Cancel_Click(object sender,RoutedEventArgs e)
    {
        CopyFrom(_settings, _original);
        _onPreview(_settings);
        DialogResult = false;
        Close();
    }

    private static HudSettings Clone(HudSettings source) => new()
    {
        Enabled = source.Enabled,
        LayoutMode = source.LayoutMode,
        ShowSpeed = source.ShowSpeed,
        ShowRpm = source.ShowRpm,
        ShowRange = source.ShowRange,
        ShowOdometer = source.ShowOdometer,
        ShowTripKm = source.ShowTripKm,
        ShowRoute = source.ShowRoute,
        ShowCompanies = source.ShowCompanies,
        ShowProgress = source.ShowProgress,
        ShowCargo = source.ShowCargo,
        ShowProfit = source.ShowProfit,
        ShowExpenses = source.ShowExpenses,
        ShowTripState = source.ShowTripState, ShowEta = source.ShowEta, ShowFuel = source.ShowFuel, ShowGear = source.ShowGear, ShowAlerts = source.ShowAlerts, ShowConnection = source.ShowConnection, CompactMode = source.CompactMode,
        Position = source.Position, UseCustomPosition = source.UseCustomPosition, CustomX = source.CustomX, CustomY = source.CustomY,
        Opacity = source.Opacity,
        Scale = source.Scale
    };

    private static void CopyFrom(HudSettings target, HudSettings source)
    {
        target.Enabled = source.Enabled;
        target.LayoutMode = source.LayoutMode;
        target.ShowSpeed = source.ShowSpeed;
        target.ShowRpm = source.ShowRpm;
        target.ShowRange = source.ShowRange;
        target.ShowOdometer = source.ShowOdometer;
        target.ShowTripKm = source.ShowTripKm;
        target.ShowRoute = source.ShowRoute;
        target.ShowCompanies = source.ShowCompanies;
        target.ShowProgress = source.ShowProgress;
        target.ShowCargo = source.ShowCargo;
        target.ShowProfit = source.ShowProfit;
        target.ShowExpenses = source.ShowExpenses;
        target.ShowTripState = source.ShowTripState; target.ShowEta = source.ShowEta; target.ShowFuel = source.ShowFuel; target.ShowGear = source.ShowGear; target.ShowAlerts = source.ShowAlerts; target.ShowConnection = source.ShowConnection; target.CompactMode = source.CompactMode;
        target.Position = source.Position; target.UseCustomPosition = source.UseCustomPosition; target.CustomX = source.CustomX; target.CustomY = source.CustomY;
        target.Opacity = source.Opacity;
        target.Scale = source.Scale;
    }
}