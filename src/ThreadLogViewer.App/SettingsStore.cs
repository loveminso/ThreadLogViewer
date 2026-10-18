using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace ThreadLogViewer.App;

public sealed record SettingsLoadResult(UiSettings Settings, bool Success, string Message, bool CanSave);
public sealed record SettingsSaveResult(bool Success, string Message);

/// <summary>
/// Portable display preferences. The exe-folder file stays the default.
/// A second file under LocalApplicationData is used only when that file cannot be created.
/// Only this application's marked, supported file can be replaced.
/// </summary>
public sealed class SettingsStore
{
    public const string OwnerMarker = "ThreadLogViewer.UiSettings";
    public const int SchemaVersion = 1;
    private const int MaximumBytes = 64 * 1024;
    private const string SaveFailedMessage = "설정을 저장하지 못했습니다. 현재 화면 설정은 유지됩니다.";
    private const string LoadFailedMessage = "설정을 읽지 못했습니다. 기존 파일을 보존합니다.";
    private const string SavedMessage = "표시 설정을 저장했습니다.";
    private readonly string applicationDirectory;
    private readonly string? fallbackRoot;
    private readonly object gate = new();
    public string FilePath { get; }
    public string? FallbackFilePath { get; }

    public SettingsStore(string applicationDirectory, string? fallbackRoot = null)
    {
        this.applicationDirectory = Path.GetFullPath(applicationDirectory);
        FilePath = Path.Combine(this.applicationDirectory, "user-data", "ui-settings.json");
        // A missing fallback root disables the second location. Production passes LocalApplicationData explicitly.
        this.fallbackRoot = ResolveFallbackRoot(fallbackRoot);
        FallbackFilePath = this.fallbackRoot is null
            ? null
            : Path.Combine(this.fallbackRoot, "ThreadLogViewer", "ui-settings.json");
    }

    public SettingsLoadResult Load()
    {
        lock (gate)
        {
            try
            {
                if (LocationOccupied(FilePath)) return LoadFile(PrimaryLocation());
                if (FallbackFilePath is not null && LocationOccupied(FallbackFilePath)) return LoadFile(FallbackLocation());
                return new(UiSettings.Default, true, "기본 표시 설정을 사용합니다.", true);
            }
            catch (Exception ex) when (IsExpectedFailure(ex))
            {
                return new(UiSettings.Default, false, LoadFailedMessage, false);
            }
        }
    }

    public SettingsSaveResult Save(UiSettings settings, string? sourcePath = null)
    {
        lock (gate)
        {
            try
            {
                UiSettings normalized = settings.Normalize();
                var primary = TrySave(PrimaryLocation(), normalized, sourcePath);
                if (primary.Kind == SaveKind.Saved) return new(true, primary.Message);
                // An existing exe-folder file is what the next launch reads. Falling back would
                // hide a newer copy that Load will not prefer, so a lock or a failed replace stays a retry.
                if (primary.Kind == SaveKind.Unwritable && FallbackFilePath is not null)
                {
                    var fallback = TrySave(FallbackLocation(), normalized, sourcePath);
                    if (fallback.Kind == SaveKind.Saved) return new(true, fallback.Message);
                    return new(false, SaveFailedMessage);
                }
                return new(false, PathFree(primary.Message));
            }
            catch (Exception)
            {
                return new(false, SaveFailedMessage);
            }
        }
    }

    private SaveAttempt TrySave(SettingsLocation location, UiSettings settings, string? sourcePath)
    {
        string? temporary = null;
        try
        {
            ValidateSource(location, sourcePath);
            var existing = LoadFile(location);
            if (!existing.CanSave) return SaveAttempt.Rejected(existing.Message);
            // Never create the exe folder or the fallback root, only the dedicated settings directory.
            if (!Directory.Exists(location.RequiredRoot)) throw new IOException("프로그램 폴더가 없습니다.");
            Directory.CreateDirectory(location.Folder);
            ValidateDirectory(location.Folder);
            temporary = Path.Combine(location.Folder, $".ui-settings-{Guid.NewGuid():N}.tmp");
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(new SettingsEnvelope
            {
                Owner = OwnerMarker, Schema = SchemaVersion, Settings = settings
            }, new JsonSerializerOptions { WriteIndented = true });
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }
            // Recheck ownership and source aliases immediately before the atomic replacement.
            ValidateSource(location, sourcePath);
            existing = LoadFile(location);
            if (!existing.CanSave) return SaveAttempt.Rejected(existing.Message);
            if (File.Exists(location.FilePath)) File.Replace(temporary, location.FilePath, null);
            else File.Move(temporary, location.FilePath, overwrite: false);
            temporary = null;
            return SaveAttempt.Saved(SavedMessage);
        }
        catch (ProtectedSettingsException ex)
        {
            return SaveAttempt.Rejected(WithReason(ex.Message));
        }
        catch (Exception ex) when (IsExpectedFailure(ex))
        {
            if (!File.Exists(location.FilePath) && IsLocationFailure(ex)) return SaveAttempt.Unwritable(SaveFailedMessage);
            return SaveAttempt.Rejected(SaveFailedMessage);
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

    private SettingsLoadResult LoadFile(SettingsLocation location)
    {
        ValidateDirectory(location.Folder);
        if (Directory.Exists(location.FilePath)) throw new ProtectedSettingsException("설정 파일 경로에 폴더가 있습니다.");
        if (!File.Exists(location.FilePath)) return new(UiSettings.Default, true, "기본 표시 설정을 사용합니다.", true);
        RejectReparse(location.FilePath);
        using var stream = new FileStream(location.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (Identity(stream).Links > 1) throw new ProtectedSettingsException("링크된 설정 파일은 보존하며 읽거나 갱신하지 않습니다.");
        if (stream.Length == 0 || stream.Length > MaximumBytes) throw new ProtectedSettingsException("설정 파일 크기가 허용 범위를 벗어납니다.");
        byte[] bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        var file = JsonSerializer.Deserialize<SettingsEnvelope>(bytes, new JsonSerializerOptions { MaxDepth = 8 });
        if (file is null || file.Owner != OwnerMarker || file.Schema != SchemaVersion || file.Settings is null)
            return new(UiSettings.Default, false, "앱 소유의 지원되는 설정 파일이 아닙니다. 기존 파일을 보존합니다.", false);
        UiSettings normalized = file.Settings.Normalize();
        return new(normalized, true, normalized == file.Settings ? "표시 설정을 복원했습니다." : "범위를 벗어난 표시 설정을 기본값으로 복원했습니다.", true);
    }

    private SettingsLocation PrimaryLocation() => new(FilePath, Path.GetDirectoryName(FilePath)!, applicationDirectory);

    private SettingsLocation FallbackLocation()
    {
        string file = FallbackFilePath ?? throw new InvalidOperationException("대체 설정 위치가 없습니다.");
        return new(file, Path.GetDirectoryName(file)!, fallbackRoot!);
    }

    private static string? ResolveFallbackRoot(string? fallbackRoot)
    {
        if (string.IsNullOrWhiteSpace(fallbackRoot)) return null;
        string full = Path.GetFullPath(fallbackRoot);
        string file = Path.GetFullPath(Path.Combine(full, "ThreadLogViewer", "ui-settings.json"));
        string prefix = full.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static bool LocationOccupied(string path) => File.Exists(path) || Directory.Exists(path);

    private void ValidateSource(SettingsLocation location, string? sourcePath)
    {
        ValidateDirectory(location.Folder);
        if (sourcePath is null) return;
        string source = Path.GetFullPath(sourcePath);
        if (string.Equals(source.TrimEnd(Path.DirectorySeparatorChar), location.FilePath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ProtectedSettingsException("현재 원본 로그와 설정 저장 경로가 같아 저장하지 않습니다.");
        if (!File.Exists(location.FilePath) || !File.Exists(source)) return;
        RejectReparse(location.FilePath);
        using var target = new FileStream(location.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var original = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var targetIdentity = Identity(target);
        if (targetIdentity.Links > 1 || targetIdentity.SameFile(Identity(original)))
            throw new ProtectedSettingsException("설정 경로가 원본 파일의 별칭 또는 링크이므로 저장하지 않습니다.");
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
            throw new ProtectedSettingsException("링크 또는 reparse point 경로의 설정은 보존하며 갱신하지 않습니다.");
    }

    private static bool IsLocationFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or
        SecurityException or Win32Exception or NotSupportedException;

    private static bool IsExpectedFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or JsonException or Win32Exception or SecurityException;

    private static bool ContainsPath(string text) => text.Contains('\\') || text.Contains('/');

    private static string WithReason(string reason) =>
        string.IsNullOrWhiteSpace(reason) || ContainsPath(reason) ? SaveFailedMessage : SaveFailedMessage + ": " + reason;

    private static string PathFree(string message) => ContainsPath(message) ? SaveFailedMessage : message;

    private sealed class ProtectedSettingsException : IOException
    {
        public ProtectedSettingsException(string message) : base(message) { }
    }

    private enum SaveKind { Saved, Rejected, Unwritable }

    private readonly record struct SaveAttempt(SaveKind Kind, string Message)
    {
        public static SaveAttempt Saved(string message) => new(SaveKind.Saved, message);
        public static SaveAttempt Rejected(string message) => new(SaveKind.Rejected, message);
        public static SaveAttempt Unwritable(string message) => new(SaveKind.Unwritable, message);
    }

    private readonly record struct SettingsLocation(string FilePath, string Folder, string RequiredRoot);

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
