using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TransPoli.GameSave;

public sealed class GameSiiReader
{
    public async Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 64 * 1024,
                    options: FileOptions.SequentialScan | FileOptions.Asynchronous);

                using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
                return await reader.ReadToEndAsync(cancellationToken);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(150 * attempt, cancellationToken);
            }
            catch (UnauthorizedAccessException) when (attempt < maxAttempts)
            {
                await Task.Delay(150 * attempt, cancellationToken);
            }
        }

        return null;
    }
}
