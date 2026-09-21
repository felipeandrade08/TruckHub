using System;

namespace TransPoli;

public partial class MainWindow
{
    private string? _tripRouteOrigin;
    private string? _tripRouteDestination;
    private string? _tripRouteOriginCompany;
    private string? _tripRouteDestinationCompany;
    private string? _tripCargo;
    private ulong? _tripCargoValue;
    private float _tripPlannedDistanceKm;
    private double _tripMovingSeconds;
    private float _tripDistanceKm;
    private float _tripFuelConsumedL;
    private float _tripLastFuelLiters;
    private DateTime _tripLastProgressAtUtc = DateTime.UtcNow;
}
