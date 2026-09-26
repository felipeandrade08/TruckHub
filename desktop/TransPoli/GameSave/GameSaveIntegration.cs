using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TransPoli.GameSave;

/// <summary>
/// Phase F bridge between game.sii persistence and the TransPoli UI.
/// Keeps save-file data separate from real-time telemetry and the TransPoli economy.
/// </summary>
public sealed class GameSaveIntegration
{
    private readonly GameSiiReader _reader = new();
    private readonly GameSiiParser _parser = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GameSaveSnapshot? LastSnapshot { get; private set; }
    public GameSiiFileInfo? LastFile { get; private set; }

    public async Task<GameSaveSnapshot?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return LastSnapshot;

        try
        {
            var path = GameSiiLocator.FindLatestGameSii();
            if (string.IsNullOrWhiteSpace(path))
                return LastSnapshot;

            var text = await _reader.ReadTextAsync(path, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
                return LastSnapshot;

            var info = new FileInfo(path);
            LastFile = new GameSiiFileInfo(path, info.LastWriteTimeUtc, info.Length);
            LastSnapshot = _parser.ParseSnapshot(text);
            return LastSnapshot;
        }
        catch (IOException)
        {
            return LastSnapshot;
        }
        catch (UnauthorizedAccessException)
        {
            return LastSnapshot;
        }
        finally
        {
            _gate.Release();
        }
    }
}
