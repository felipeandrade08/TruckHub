using System;
using System.IO;

namespace TransPoli.Navigation;

/// <summary>
/// Descobre uma calibração já extraída pelo usuário/instalador.
/// O perfil fica fora do executável para não amarrar o TransPoli a uma versão específica do ETS2.
/// </summary>
public static class MapCalibrationProvider
{
    public static string DefaultFilePath
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TransPoli",
            "Navigation",
            "climate.sii");

    public static bool TryLoad(out MapCalibration calibration)
        => ClimateProfileParser.TryReadFile(DefaultFilePath, out calibration);

    public static bool TryLoad(string path, out MapCalibration calibration)
        => ClimateProfileParser.TryReadFile(path, out calibration);
}
