using System.Text;

namespace ThreadLogViewer.Core;

public static class LogExporter
{
    public const string EncodingDescription = "UTF-8 (BOM 없음)";

    public static void ValidateDestination(string? sourcePath, string destinationPath)
    {
        string destination = Path.GetFullPath(destinationPath).TrimEnd(Path.DirectorySeparatorChar);
        string? source = sourcePath is null ? null : Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar);
        if (source is not null && string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            throw new IOException("원본 파일과 같은 경로로 내보낼 수 없습니다.");
        // Conservative policy prevents hard links, short-path aliases, and symlink aliases from
        // overwriting the source: v0.1 only creates a NEW destination, never truncates a file.
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("원본 보호를 위해 새 파일 이름을 사용해 주세요. 기존 파일은 덮어쓰지 않습니다.");
    }

    public static async Task ExportAsync(LogProjection projection, string destinationPath,
        CancellationToken cancellationToken = default, IProgress<WorkProgress>? progress = null)
    {
        ValidateDestination(projection.Source.SourcePath, destinationPath);
        string destination = Path.GetFullPath(destinationPath);
        string temp = Path.Combine(Path.GetDirectoryName(destination)!, $".threadlog-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true)))
            {
                for (int i = 0; i < projection.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = projection.Source.Lines[projection.SourceIndexes[i]];
                    await writer.WriteAsync(line.RawText, cancellationToken).ConfigureAwait(false);
                    await writer.WriteAsync(line.LineEnding, cancellationToken).ConfigureAwait(false);
                    if ((i & 4095) == 0) progress?.Report(new("필터 결과 내보내기", 100.0 * i / Math.Max(1, projection.Count)));
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            ValidateDestination(projection.Source.SourcePath, destination);
            File.Move(temp, destination, overwrite: false);
            progress?.Report(new("필터 결과 내보내기", 100));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
