using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ThreadLogViewer.App;

public sealed record SettingsLoadResult(UiSettings Settings, bool Success, string Message, bool CanSave);
public sealed record SettingsSaveResult(bool Success, string Message);

/// <summary>Portable display preferences. Only this application's marked, supported file can be replaced.</summary>
public sealed class SettingsStore
{
    public const string OwnerMarker = "ThreadLogViewer.UiSettings";
    public const int SchemaVersion = 1;
    private const int MaximumBytes = 64 * 1024;
    private readonly string applicationDirectory;
    private readonly object gate = new();
    public string FilePath { get; }

    public SettingsStore(string applicationDirectory)
    {
        this.applicationDirectory = Path.GetFullPath(applicationDirectory);
        FilePath = Path.Combine(this.applicationDirectory, "user-data", "ui-settings.json");
    }

    public SettingsLoadResult Load()
    {
        lock (gate)
        {
            try { return LoadCore(); }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                return new(UiSettings.Default, false, $"설정을 읽지 못했습니다. 기존 파일을 보존합니다: {ex.Message}", false);
            }
        }
    }

    public SettingsSaveResult Save(UiSettings settings, string? sourcePath = null)
    {
        lock (gate)
        {
            string? temporary = null;
            try
            {
                ValidateSource(sourcePath);
                var existing = LoadCore();
                if (!existing.CanSave) return new(false, existing.Message);
                string folder = Path.GetDirectoryName(FilePath)!;
                // Never create an application root, only its dedicated settings directory.
                if (!Directory.Exists(applicationDirectory)) throw new IOException("프로그램 폴더가 없습니다.");
                Directory.CreateDirectory(folder);
                ValidateDirectory(folder);
                temporary = Path.Combine(folder, $".ui-settings-{Guid.NewGuid():N}.tmp");
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(new SettingsEnvelope
                {
                    Owner = OwnerMarker, Schema = SchemaVersion, Settings = settings.Normalize()
                }, new JsonSerializerOptions { WriteIndented = true });
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(json);
                    stream.Flush(flushToDisk: true);
                }
                // Recheck ownership and source aliases immediately before the atomic replacement.
                ValidateSource(sourcePath);
                existing = LoadCore();
                if (!existing.CanSave) return new(false, existing.Message);
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath, overwrite: false);
                temporary = null;
                return new(true, "표시 설정을 저장했습니다.");
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                return new(false, $"설정을 저장하지 못했습니다. 현재 화면 설정은 유지됩니다: {ex.Message}");
            }
            finally
            {
                if (temporary is not null)
                {
                    try { File.Delete(temporary); }
                    catch (Exception ex) when (IsExpectedFailure(ex)) { /* Preserve the original failure. */ }
                }
            }
        }
    }

    private SettingsLoadResult LoadCore()
    {
        ValidateDirectory(Path.GetDirectoryName(FilePath)!);
        if (Directory.Exists(FilePath)) throw new IOException("설정 파일 경로에 폴더가 있습니다.");
        if (!File.Exists(FilePath)) return new(UiSettings.Default, true, "기본 표시 설정을 사용합니다.", true);
        RejectReparse(FilePath);
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Identity(stream).Links > 1) throw new IOException("링크된 설정 파일은 보존하며 읽거나 갱신하지 않습니다.");
        if (stream.Length == 0 || stream.Length > MaximumBytes) throw new IOException("설정 파일 크기가 허용 범위를 벗어납니다.");
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var file = JsonSerializer.Deserialize<SettingsEnvelope>(bytes, new JsonSerializerOptions { MaxDepth = 8 });
        if (file is null || file.Owner != OwnerMarker || file.Schema != SchemaVersion || file.Settings is null)
            return new(UiSettings.Default, false, "앱 소유의 지원되는 설정 파일이 아닙니다. 기존 파일을 보존합니다.", false);
        UiSettings normalized = file.Settings.Normalize();
        return new(normalized, true, normalized == file.Settings ? "표시 설정을 복원했습니다." : "범위를 벗어난 표시 설정을 기본값으로 복원했습니다.", true);
    }

    private void ValidateSource(string? sourcePath)
    {
        ValidateDirectory(Path.GetDirectoryName(FilePath)!);
        if (sourcePath is null) return;
        string source = Path.GetFullPath(sourcePath);
        if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar), FilePath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new IOException("현재 원본 로그와 설정 저장 경로가 같아 저장하지 않습니다.");
        if (!File.Exists(FilePath) || !File.Exists(source)) return;
        RejectReparse(FilePath);
        using var target = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var original = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var targetIdentity = Identity(target);
        if (targetIdentity.Links > 1 || targetIdentity.SameFile(Identity(original)))
            throw new IOException("설정 경로가 원본 파일의 별칭 또는 링크이므로 저장하지 않습니다.");
    }

    private static void ValidateDirectory(string folder)
    {
        for (string? current = folder; current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current)) throw new IOException("설정 폴더 경로에 파일이 있습니다.");
            if (Directory.Exists(current)) RejectReparse(current);
        }
    }

    private static void RejectReparse(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("링크 또는 reparse point 경로의 설정은 보존하며 갱신하지 않습니다.");
    }

    private static bool IsExpectedFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or JsonException or Win32Exception or SecurityException;

    private sealed class SettingsEnvelope
    {
        public string? Owner { get; init; }
        public int Schema { get; init; }
        public UiSettings? Settings { get; init; }
    }

    private readonly record struct FileIdentity(uint Volume, uint IndexHigh, uint IndexLow, uint Links)
    {
        public bool SameFile(FileIdentity other) => Volume == other.Volume && IndexHigh == other.IndexHigh && IndexLow == other.IndexLow;
    }

    private static FileIdentity Identity(FileStream stream)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(info.VolumeSerialNumber, info.FileIndexHigh, info.FileIndexLow, info.NumberOfLinks);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
