namespace TransPoli.Navigation;

/// <summary>
/// Converte coordenadas nativas do mundo ETS2 para coordenadas geográficas.
/// A implementação deve ser específica para a calibração do mapa em uso.
/// </summary>
public interface IWorldCoordinateConverter
{
    bool TryConvert(
        double worldX,
        double worldZ,
        out double latitude,
        out double longitude);
}
