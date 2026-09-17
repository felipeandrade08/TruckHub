using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace TransPoliUpdater;

internal static class Program
{
    private const string DefaultManifestUrl = "https://updates.truckhub.com.br/manifest.json";
    private const long MaxPackageBytes = 500L * 1024 * 1024;
    private static readonly Regex Sha256Pattern = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    public static async Task<int> Main(string[] args)
    {
        var manifestUrl = Environment.GetEnvironmentVariable("TRANSPOLI_UPDATE_MANIFEST") ?? DefaultManifestUrl;
        var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

        Console.WriteLine("TransPoli Updater - criado por Felipe Andrade");
        Console.WriteLine($"Versão atual: {currentVersion}");
        Console.WriteLine($"Manifesto: {manifestUrl}");

        if (args.Contains("--version", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(currentVersion);
            return 0;
        }

        var checkOnly = args.Contains("--check", StringComparer.OrdinalIgnoreCase);
        var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
        if (!checkOnly && !apply)
        {
            Console.WriteLine("Use --check para consultar ou --apply para instalar uma atualização.");
            return 0;
        }

        return await RunAsync(manifestUrl, currentVersion, apply);
    }

    private static async Task<int> RunAsync(string manifestUrl, string currentVersion, bool apply)
    {
        try
        {
            if (!Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri) || manifestUri.Scheme != Uri.UriSchemeHttps)
            {
                Console.WriteLine("Manifesto recusado: HTTPS obrigatório.");
                return 2;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TransPoliUpdater/0.3.1");

            var manifest = await http.GetFromJsonAsync<UpdateManifest>(manifestUri);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
            {
                Console.WriteLine("Manifesto inválido.");
                return 2;
            }

            Console.WriteLine($"Última versão: {manifest.Version}");
            Console.WriteLine($"Canal: {manifest.Channel ?? "stable"}");
            Console.WriteLine($"Obrigatória: {manifest.Mandatory}");

            if (!Version.TryParse(currentVersion, out var current) || !Version.TryParse(manifest.Version, out var latest))
            {
                Console.WriteLine("Não foi possível comparar as versões.");
                return 2;
            }

            if (latest <= current)
            {
                Console.WriteLine("UP_TO_DATE");
                return 0;
            }

            Console.WriteLine("UPDATE_AVAILABLE");
            Console.WriteLine($"Download: {manifest.DownloadUrl ?? "não informado"}");
            Console.WriteLine($"Checksum SHA-256: {manifest.ChecksumSha256 ?? "não informado"}");

            if (!apply) return 0;
            if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri) || downloadUri.Scheme != Uri.UriSchemeHttps)
            {
                Console.WriteLine("Atualização recusada: URL HTTPS inválida.");
                return 3;
            }
            if (string.IsNullOrWhiteSpace(manifest.ChecksumSha256) || !Sha256Pattern.IsMatch(manifest.ChecksumSha256.Trim()))
            {
                Console.WriteLine("Atualização recusada: SHA-256 inválido.");
                return 3;
            }

            return await DownloadAndInstallAsync(http, downloadUri, manifest);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Falha na atualização: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> DownloadAndInstallAsync(HttpClient http, Uri downloadUri, UpdateManifest manifest)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "TransPoliUpdater", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var packagePath = Path.Combine(tempRoot, "transpoli-update.zip");

        try
        {
            Console.WriteLine("Baixando pacote...");
            using var response = await http.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxPackageBytes)
            {
                Console.WriteLine("Atualização recusada: pacote maior que o limite permitido.");
                return 4;
            }

            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var target = File.Create(packagePath))
            {
                await CopyWithLimitAsync(source, target, MaxPackageBytes);
            }

            var actualHash = await Sha256Async(packagePath);
            var expectedHash = manifest.ChecksumSha256!.Trim().ToLowerInvariant();
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Checksum inválido. Recebido: {actualHash}");
                return 5;
            }

            var installRoot = Environment.GetEnvironmentVariable("TRANSPOLI_INSTALL_DIR");
            if (string.IsNullOrWhiteSpace(installRoot))
                installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TransPoli");
            installRoot = Path.GetFullPath(installRoot);
            Directory.CreateDirectory(installRoot);

            var staging = Path.Combine(tempRoot, "staging");
            ZipFile.ExtractToDirectory(packagePath, staging, overwriteFiles: true);

            var payloadRoot = Directory.GetFiles(staging, "TransPoli.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (payloadRoot is null)
            {
                Console.WriteLine("Pacote inválido: TransPoli.exe não encontrado.");
                return 6;
            }

            var sourceRoot = Path.GetDirectoryName(payloadRoot)!;
            var backupRoot = Path.Combine(installRoot, "backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            Directory.CreateDirectory(backupRoot);

            BackupExistingFiles(installRoot, backupRoot);
            CopyDirectoryRecursive(sourceRoot, installRoot);

            Console.WriteLine($"Atualização {manifest.Version} instalada.");
            Console.WriteLine($"Backup: {backupRoot}");
            return 0;
        }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"Pacote inválido: {ex.Message}");
            return 6;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Instalação falhou: {ex.Message}");
            return 7;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Instalação falhou: {ex.Message}");
            return 8;
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream target, long maxBytes)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer);
            if (read == 0) break;
            total += read;
            if (total > maxBytes) throw new InvalidDataException("Pacote excede o limite permitido.");
            await target.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private static void BackupExistingFiles(string installRoot, string backupRoot)
    {
        foreach (var directory in Directory.GetDirectories(installRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(directory);
            if (name.StartsWith("backup-", StringComparison.OrdinalIgnoreCase)) continue;
            CopyDirectoryRecursive(directory, Path.Combine(backupRoot, name));
        }

        foreach (var file in Directory.GetFiles(installRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("backup-", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, Path.Combine(backupRoot, name), true);
        }
    }

    private static void CopyDirectoryRecursive(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.TopDirectoryOnly))
            CopyDirectoryRecursive(directory, Path.Combine(destination, Path.GetFileName(directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public string? Channel { get; set; }
        public string? DownloadUrl { get; set; }
        public string? ChecksumSha256 { get; set; }
        public bool Mandatory { get; set; }
        public string? Changelog { get; set; }
    }
}
