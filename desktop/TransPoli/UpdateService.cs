using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace TransPoli;

public partial class MainWindow
{
    // Releases e manifesto ficam no mesmo repositório do projeto.
    private const string UpdateRepository = "felipeandrade08/TruckHub";
    private static string ManifestUrl => Environment.GetEnvironmentVariable("TRANSPOLI_UPDATE_MANIFEST") ?? $"https://github.com/{UpdateRepository}/releases/latest/download/manifest.json";
    private const long MaxPackageBytes = 500L * 1024 * 1024;
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(15) };
    private DispatcherTimer? _updateTimer;
    private bool _updateBusy;
    private static string CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private void StartUpdateWatcher()
    {
        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync(false);
        _updateTimer.Start();
        var firstCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        firstCheck.Tick += async (_, _) => { firstCheck.Stop(); await CheckForUpdatesAsync(false); };
        firstCheck.Start();
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateBusy) return;
        _updateBusy = true;
        try
        {
            if (manual) StatusText.Text = "Procurando atualizações...";
            var manifest = await DownloadManifestAsync();
            if (manifest is null)
            {
                if (manual) ShowUpdateMessage("Não foi possível consultar as atualizações agora. Verifique sua conexão e tente de novo.");
                return;
            }
            if (!IsNewer(manifest.Version, CurrentVersion))
            {
                if (manual) ShowUpdateMessage($"Você já está na versão mais recente ({CurrentVersion}).");
                else StatusText.Text = $"TransPoli {CurrentVersion} • nenhuma atualização pendente";
                return;
            }
            var changelog = string.IsNullOrWhiteSpace(manifest.Changelog) ? "" : $"\n\nNovidades:\n{manifest.Changelog}";
            var question = $"Uma nova versão do TransPoli está disponível.\n\nInstalada: {CurrentVersion}\nNova: {manifest.Version}{changelog}\n\nA atualização é aplicada por cima da instalação atual e o app reabre sozinho.\n\nDeseja atualizar agora?";
            if (!manifest.Mandatory)
            {
                var answer = MessageBox.Show(question, "TransPoli • Atualização", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (answer != MessageBoxResult.Yes)
                {
                    StatusText.Text = $"Atualização {manifest.Version} disponível • use o botão ATUALIZAR APP quando quiser";
                    return;
                }
            }
            else MessageBox.Show($"Atualização obrigatória para a versão {manifest.Version}.{changelog}\n\nO TransPoli será atualizado e reaberto automaticamente.", "TransPoli • Atualização obrigatória", MessageBoxButton.OK, MessageBoxImage.Warning);
            await DownloadAndInstallAsync(manifest);
        }
        catch (Exception ex)
        {
            if (manual) ShowUpdateMessage($"Falha ao atualizar: {ex.Message}");
            else StatusText.Text = "Não foi possível verificar atualizações agora.";
        }
        finally { _updateBusy = false; }
    }

    private async Task<UpdateManifest?> DownloadManifestAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUrl);
            request.Headers.UserAgent.ParseAdd($"TransPoli/{CurrentVersion}");
            using var response = await _updateHttp.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.DownloadUrl)) return null;
            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
            return manifest;
        }
        catch { return null; }
    }

    private async Task DownloadAndInstallAsync(UpdateManifest manifest)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "TransPoli-update");
        Directory.CreateDirectory(tempRoot);
        var setupPath = Path.Combine(tempRoot, "TransPoli-Setup.exe");
        try { if (File.Exists(setupPath)) File.Delete(setupPath); } catch { }
        StatusText.Text = $"Baixando a atualização {manifest.Version}...";
        using (var response = await _updateHttp.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1L;
            if (total > MaxPackageBytes) throw new InvalidOperationException("Pacote de atualização maior que o limite permitido.");
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var target = new FileStream(setupPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920]; long received = 0; int read;
            while ((read = await source.ReadAsync(buffer)) > 0)
            {
                received += read;
                if (received > MaxPackageBytes) throw new InvalidOperationException("Pacote de atualização maior que o limite permitido.");
                await target.WriteAsync(buffer.AsMemory(0, read));
                if (total > 0) StatusText.Text = $"Baixando a atualização {manifest.Version}... {(int)(received * 100 / total)}%";
            }
        }
        if (!string.IsNullOrWhiteSpace(manifest.ChecksumSha256))
        {
            StatusText.Text = "Verificando o pacote baixado...";
            var hash = await ComputeSha256Async(setupPath);
            if (!string.Equals(hash, manifest.ChecksumSha256!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(setupPath); } catch { }
                throw new InvalidOperationException("O arquivo baixado não confere com o SHA-256 publicado.");
            }
        }
        StatusText.Text = "Instalando a atualização... o TransPoli vai reabrir sozinho.";
        try
        {
            foreach (var process in Process.GetProcessesByName("TransPoliConnector"))
            {
                try { process.Kill(true); process.WaitForExit(4000); } catch { }
            }
        }
        catch { }
        var logPath = Path.Combine(tempRoot, "instalacao.log");
        var startInfo = new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = $"/SILENT /SUPPRESSMSGBOXES /NOCANCEL /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /LOG=\"{logPath}\"",
            UseShellExecute = true,
            WorkingDirectory = tempRoot
        };
        Process.Start(startInfo);
        await Task.Delay(1200);
        Application.Current.Shutdown();
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream));
    }

    private static bool IsNewer(string? remote, string local)
    {
        if (string.IsNullOrWhiteSpace(remote)) return false;
        var cleaned = remote.Trim().TrimStart('v', 'V');
        return Version.TryParse(cleaned, out var remoteVersion) && Version.TryParse(local, out var localVersion) && remoteVersion > localVersion;
    }

    private void ShowUpdateMessage(string message) => MessageBox.Show(message, "TransPoli • Atualização", MessageBoxButton.OK, MessageBoxImage.Information);

    private sealed class UpdateManifest
    {
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("channel")] public string? Channel { get; set; }
        [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; set; }
        [JsonPropertyName("checksumSha256")] public string? ChecksumSha256 { get; set; }
        [JsonPropertyName("mandatory")] public bool Mandatory { get; set; }
        [JsonPropertyName("changelog")] public string? Changelog { get; set; }
    }
}
