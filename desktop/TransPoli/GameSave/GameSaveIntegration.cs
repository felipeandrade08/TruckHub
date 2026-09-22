using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TransPoli.GameSave;

/// <summary>
/// Phase F bridge between game.sii persistence and the TransPoli UI.
/// Keeps save-file data separate from real-time telemetry and TruckHub economy.
/// </summary>
public sealed class GameSaveIntegration
{
    private readonly GameSiiLocator _locator = new();
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
            var file = _locator.FindLatestGameSii();
            if (file is null)
                return LastSnapshot;

            var text = await _reader.ReadTextAsync(file.Path, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
                return LastSnapshot;

            LastFile = file;
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
