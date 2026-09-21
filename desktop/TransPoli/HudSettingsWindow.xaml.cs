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
        EnabledCheck.IsChecked=_settings.Enabled; SpeedCheck.IsChecked=_settings.ShowSpeed; OdometerCheck.IsChecked=_settings.ShowOdometer; TripKmCheck.IsChecked=_settings.ShowTripKm;
        RouteCheck.IsChecked=_settings.ShowRoute; CompaniesCheck.IsChecked=_settings.ShowCompanies; ProgressCheck.IsChecked=_settings.ShowProgress; CargoCheck.IsChecked=_settings.ShowCargo;
        ProfitCheck.IsChecked=_settings.ShowProfit; ExpensesCheck.IsChecked=_settings.ShowExpenses;
        PositionCombo.SelectedIndex=_settings.Position=="Topo"?0:_settings.Position=="Inferior"?2:1; OpacitySlider.Value=_settings.Opacity; ScaleSlider.Value=_settings.Scale; RefreshLabels();
    }
    private void ReadValues()
    {
        _settings.Enabled=EnabledCheck.IsChecked==true; _settings.ShowSpeed=SpeedCheck.IsChecked==true; _settings.ShowOdometer=OdometerCheck.IsChecked==true; _settings.ShowTripKm=TripKmCheck.IsChecked==true;
        _settings.ShowRoute=RouteCheck.IsChecked==true; _settings.ShowCompanies=CompaniesCheck.IsChecked==true; _settings.ShowProgress=ProgressCheck.IsChecked==true; _settings.ShowCargo=CargoCheck.IsChecked==true;
        _settings.ShowProfit=ProfitCheck.IsChecked==true; _settings.ShowExpenses=ExpensesCheck.IsChecked==true;
        _settings.Position=(PositionCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString()??"Centro superior"; _settings.Opacity=OpacitySlider.Value; _settings.Scale=ScaleSlider.Value;
    }
    private void SettingChanged(object sender,RoutedEventArgs e){ApplyPreview();}
    private void SettingChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e){ApplyPreview();}
    private void SettingChanged(object sender,RoutedPropertyChangedEventArgs<double> e){ApplyPreview();}
    private void ApplyPreview(){if(_loading)return; ReadValues(); RefreshLabels(); _onPreview(_settings);}
    private void RefreshLabels(){OpacityText.Text=$"{_settings.Opacity*100:0}%"; ScaleText.Text=$"{_settings.Scale*100:0}%";}
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
        ShowSpeed = source.ShowSpeed,
        ShowOdometer = source.ShowOdometer,
        ShowTripKm = source.ShowTripKm,
        ShowRoute = source.ShowRoute,
        ShowCompanies = source.ShowCompanies,
        ShowProgress = source.ShowProgress,
        ShowCargo = source.ShowCargo,
        ShowProfit = source.ShowProfit,
        ShowExpenses = source.ShowExpenses,
        Position = source.Position,
        Opacity = source.Opacity,
        Scale = source.Scale
    };

    private static void CopyFrom(HudSettings target, HudSettings source)
    {
        target.Enabled = source.Enabled;
        target.ShowSpeed = source.ShowSpeed;
        target.ShowOdometer = source.ShowOdometer;
        target.ShowTripKm = source.ShowTripKm;
        target.ShowRoute = source.ShowRoute;
        target.ShowCompanies = source.ShowCompanies;
        target.ShowProgress = source.ShowProgress;
        target.ShowCargo = source.ShowCargo;
        target.ShowProfit = source.ShowProfit;
        target.ShowExpenses = source.ShowExpenses;
        target.Position = source.Position;
        target.Opacity = source.Opacity;
        target.Scale = source.Scale;
    }
}