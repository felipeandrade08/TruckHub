using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

/// <summary>
/// 🔐 BLOQUEIO FÍSICO
///
/// Quando o caminhão está bloqueado — pelo tablet ou pela garagem — o
/// TransPoli aplica o freio de estacionamento dentro do jogo e mantém
/// ele aplicado. O veículo simplesmente não anda.
///
/// Correções sobre a versão anterior:
///   • o contador parava em 3 tentativas e nunca mais reagia, então
///     bastava soltar o freio uma quarta vez para burlar o bloqueio.
///     Agora o reforço é contínuo, com intervalo entre as tentativas;
///   • a janela do jogo era trazida para frente a cada segundo, mesmo
///     com o usuário em outro programa. Agora só age com o jogo em foco;
///   • o freio era solto automaticamente ao destravar, brigando com o
///     motorista. Soltar o freio passou a ser decisão dele.
/// </summary>
public partial class MainWindow
{
    /// <summary>Espaço — tecla padrão do freio de estacionamento no ETS2/ATS.</summary>
    private const byte ParkingBrakeKey = 0x20;
    private const uint KeyEventKeyUp = 0x0002;

    private DispatcherTimer? _physicalLockTimer;
    private DateTime _lastPhysicalBrakeCommand = DateTime.MinValue;
    private bool _physicalLockBusy;
    private int _physicalBrakeAttempts;
    private bool _lockEngagedNotified;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _physicalLockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _physicalLockTimer.Tick += async (_, _) => await EnforcePhysicalLockAsync();
        _physicalLockTimer.Start();
        StartGarageEnforcement();
    }

    private async Task EnforcePhysicalLockAsync()
    {
        if (_physicalLockBusy) return;
        _physicalLockBusy = true;
        try
        {
            var locked = _truckLocked || _garageUnauthorized;
            if (!locked)
            {
                // Destravar não solta o freio: quem faz isso é o motorista.
                _physicalBrakeAttempts = 0;
                _lockEngagedNotified = false;
                return;
            }

            TelemetrySnapshot? data;
            try
            {
                using var response = await _http.GetAsync(TelemetryUrl);
                if (!response.IsSuccessStatusCode) return;
                await using var stream = await response.Content.ReadAsStreamAsync();
                data = await JsonSerializer.DeserializeAsync<TelemetrySnapshot>(
                    stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { return; }

            if (data is null || !data.Connected || data.GamePaused) return;

            // Freio já aplicado: bloqueio cumprido.
            if (data.ParkingBrake)
            {
                _physicalBrakeAttempts = 0;
                if (!_lockEngagedNotified)
                {
                    _lockEngagedNotified = true;
                    StatusText.Text = _garageUnauthorized
                        ? "TransPoli • bloqueio físico aplicado • caminhão não autorizado"
                        : "TransPoli • bloqueio físico aplicado";
                }
                return;
            }

            _lockEngagedNotified = false;

            // Reforço contínuo, mas espaçado: nunca desiste do bloqueio.
            var cooldown = _physicalBrakeAttempts >= 4
                ? TimeSpan.FromSeconds(3)
                : TimeSpan.FromMilliseconds(900);
            if (DateTime.UtcNow - _lastPhysicalBrakeCommand < cooldown) return;

            if (!ApplyParkingBrakeKey()) return;
            _physicalBrakeAttempts++;
            _lastPhysicalBrakeCommand = DateTime.UtcNow;
        }
        catch
        {
            // Instabilidade do Connector não pode derrubar o tablet.
        }
        finally
        {
            _physicalLockBusy = false;
        }
    }

    /// <summary>
    /// Envia a tecla do freio para a janela do jogo. Só age quando o jogo
    /// já está em foco, para não roubar o teclado de outro programa.
    /// </summary>
    private static bool ApplyParkingBrakeKey()
    {
        var gameWindow = FindTruckGameWindow();
        if (gameWindow == IntPtr.Zero) return false;
        if (GetForegroundWindow() != gameWindow) return false;

        keybd_event(ParkingBrakeKey, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(45);
        keybd_event(ParkingBrakeKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        return true;
    }

    private static IntPtr FindTruckGameWindow()
    {
        foreach (var name in new[] { "eurotrucks2", "amtrucks" })
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;
                    }
                }
            }
            catch { }
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
