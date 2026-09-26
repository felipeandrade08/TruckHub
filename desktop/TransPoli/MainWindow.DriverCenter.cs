using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace TransPoli;

public partial class MainWindow
{
    private DateTime _driverCenterLastRefreshUtc = DateTime.MinValue;
    private DateTime _driverCenterLastRotationUtc = DateTime.UtcNow;
    private int _driverCenterRotationIndex;
    private DateTime _dashboardRankingLastRefreshUtc = DateTime.MinValue;

    private sealed class DriverCenterItem
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "Motorista";
        public string Truck { get; init; } = "Caminhão não identificado";
        public string Cargo { get; init; } = "Sem carga";
        public string Origin { get; init; } = "—";
        public string Destination { get; init; } = "—";
        public double SpeedKph { get; init; }
        public double TripKm { get; init; }
        public string Status { get; init; } = "DISPONÍVEL";
    }

    private Task RefreshDriverCenterAsync(bool force = false)
    {
        // A visão de outros motoristas pertence exclusivamente à Central da Diretoria.
        // O cockpit do motorista não consome mais /me/drivers/online em segundo plano.
        return Task.CompletedTask;
    }

    private void RenderDriverCenter(IReadOnlyList<DriverCenterItem> drivers)
    {
        // O cockpit 2.0 não renderiza mais cards de outros motoristas.
        // Mantemos a coleta isolada para futura Central da Diretoria, sem bindings XAML legados.
    }

}
