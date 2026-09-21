using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TransPoli;

public partial class TelemetryOverlayWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExNoactivate = 0x08000000;
    private const int WsExToolwindow = 0x80;

    public TelemetryOverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            MakeClickThrough();
            PositionAtTop();
        };
        SizeChanged += (_, _) => PositionAtTop();
    }

    public void UpdateTelemetry(TelemetrySnapshot data, bool tripActive, float tripStartOdometer, float plannedDistanceKm)
    {
        var tripKm = tripActive ? Math.Max(0, data.OdometerKm - tripStartOdometer) : 0;
        var planned = plannedDistanceKm > 0 ? plannedDistanceKm : Math.Max(tripKm, data.RouteDistanceKm);
        var progress = tripActive && planned > 0 ? Math.Clamp((tripKm / planned) * 100.0, 0, 100) : 0;

        StateText.Text = tripActive ? " • EM VIAGEM" : " • AGUARDANDO";
        StateText.Foreground = FindResource(tripActive ? "Green" : "TextMuted") as System.Windows.Media.Brush;
        TripKmText.Text = $"{tripKm:0.0}";
        OdometerText.Text = $"{data.OdometerKm:0.0}";
        SpeedText.Text = $"{Math.Abs(data.SpeedKph):0} km/h";
        RpmText.Text = $"{data.Rpm:0}";
        RangeText.Text = data.FuelRangeKm > 0 ? $"{data.FuelRangeKm:0} km" : "N/D";

        var origin = string.IsNullOrWhiteSpace(data.SourceCity) ? "Origem" : data.SourceCity;
        var destination = string.IsNullOrWhiteSpace(data.DestinationCity) ? "Destino" : data.DestinationCity;
        RouteText.Text = $"{origin}  →  {destination}";
        CompaniesText.Text = $"{Display(data.SourceCompany, "Empresa de origem")}  →  {Display(data.DestinationCompany, "Empresa de destino")}" +
                             (string.IsNullOrWhiteSpace(data.Cargo) ? "" : $"  •  {data.Cargo}");

        ProgressFill.Width = 430 * (progress / 100.0);
    }

    private static string Display(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private void PositionAtTop()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + Math.Max(0, (area.Width - Width) / 2);
        Top = area.Top + 14;
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style | WsExTransparent | WsExNoactivate | WsExToolwindow));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}