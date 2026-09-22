using System;

namespace TransPoli.Navigation;

/// <summary>
/// Estado normalizado da navegação. Mantém os dados de telemetria separados
/// do motor visual do mapa e permite trocar o provedor de mapa sem alterar a UI.
/// </summary>
public sealed record NavigationState
{
    public bool IsValid { get; init; }
    public bool PositionValid { get; init; }

    // Coordenadas nativas do mundo ETS2.
    public double WorldX { get; init; }
    public double WorldY { get; init; }
    public double WorldZ { get; init; }

    // Orientação do caminhão em graus, normalizada para [0, 360).
    public double HeadingDeg { get; init; }
    public double PitchDeg { get; init; }
    public double RollDeg { get; init; }

    // Coordenadas geográficas. Permanecem nulas até existir uma calibração
    // comprovada para a versão/mapa do ETS2 em execução.
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }

    // Navegação fornecida diretamente pela telemetria do ETS2.
    public double? RemainingDistanceKm { get; init; }
    public double? RemainingTimeMinutes { get; init; }
    public double? SpeedLimitKph { get; init; }

    public string? Origin { get; init; }
    public string? Destination { get; init; }

    public string PositionSource { get; init; } = "ETS2_WORLD";
    public string MapSource { get; init; } = "NONE";
}
