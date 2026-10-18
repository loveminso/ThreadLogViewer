using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Windows.Controls;
using System.Windows.Threading;
using ThreadLogViewer.App;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class SettingsFallbackTests
{
    [Fact]
    public void WritableExeFolderSavesBesideTheExecutableAndDoesNotCreateFallback()
    {
        using var root = new SyntheticRoot();
        string exe = Directory.CreateDirectory(Path.Combine(root.Path, "exe")).FullName;
        string fallbackRoot = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var values = Values(dark: false, fontSize: 18);
        var store = new SettingsStore(exe, fallbackRoot);

        var saved = store.Save(values);

        Assert.True(saved.Success, saved.Message);
        Assert.Equal(values, store.Load().Settings);
        Assert.Equal(values, new SettingsStore(exe, fallbackRoot).Load().Settings);
        Assert.True(File.Exists(store.FilePath));
        Assert.NotNull(store.FallbackFilePath);
        Assert.False(File.Exists(store.FallbackFilePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(store.FallbackFilePath)));
    }

    [Fact]
    public void UnwritableExeFolderSavesToFallbackAndNewStoreReadsSameValues()
    {
        using var root = new SyntheticRoot();
        string exe = Directory.CreateDirectory(Path.Combine(root.Path, "exe")).FullName;
        string fallbackRoot = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var values = Values(dark: false, fontSize: 20);
        using (new DenyWrite(exe))
        {
            AssertUnwritable(exe);
            var store = new SettingsStore(exe, fallbackRoot);
            var saved = store.Save(values);

            Assert.True(saved.Success, saved.Message);
            Assert.False(File.Exists(store.FilePath));
            Assert.NotNull(store.FallbackFilePath);
            Assert.True(File.Exists(store.FallbackFilePath));
            Assert.Equal(values, new SettingsStore(exe, fallbackRoot).Load().Settings);
        }
    }

    [Fact]
    public void BothLocationsFailReturnsPathFreeMessageWithoutThrowing()
    {
        using var root = new SyntheticRoot();
        string exe = Directory.CreateDirectory(Path.Combine(root.Path, "exe")).FullName;
        string fallbackRoot = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var values = Values(dark: false, fontSize: 16);
        using (new DenyWrite(exe))
        using (new DenyWrite(fallbackRoot))
        {
            AssertUnwritable(exe);
            AssertUnwritable(fallbackRoot);
            var store = new SettingsStore(exe, fallbackRoot);
            SettingsSaveResult result = new(true, "");
            var thrown = Record.Exception(() => result = store.Save(values));

            Assert.Null(thrown);
            Assert.False(result.Success);
            Assert.Equal("설정을 저장하지 못했습니다. 현재 화면 설정은 유지됩니다.", result.Message);
            Assert.DoesNotContain(exe, result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(fallbackRoot, result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\\", result.Message);
            Assert.DoesNotContain("/", result.Message);
            Assert.False(File.Exists(store.FilePath));
            Assert.False(File.Exists(store.FallbackFilePath));
        }
    }

    [Fact]
    public void ExeFolderFileWinsWhenFallbackFileAlsoExists()
    {
        using var root = new SyntheticRoot();
        string exe = Directory.CreateDirectory(Path.Combine(root.Path, "exe")).FullName;
        string fallbackRoot = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var exeValues = Values(dark: false, fontSize: 20);
        var fallbackValues = Values(dark: true, fontSize: 11);
        using (new DenyWrite(exe))
        {
            AssertUnwritable(exe);
            Assert.True(new SettingsStore(exe, fallbackRoot).Save(fallbackValues).Success);
        }

        var store = new SettingsStore(exe, fallbackRoot);
        Assert.True(store.Save(exeValues).Success);
        Assert.Equal(exeValues, store.Load().Settings);
        Assert.Equal(exeValues, new SettingsStore(exe, fallbackRoot).Load().Settings);
        string emptyExe = Directory.CreateDirectory(Path.Combine(root.Path, "empty-exe")).FullName;
        Assert.Equal(fallbackValues, new SettingsStore(emptyExe, fallbackRoot).Load().Settings);
    }

    [Fact]
    public void LockedExistingExeFileStaysInPlaceAndDoesNotUseFallback()
    {
        using var root = new SyntheticRoot();
        string exe = Directory.CreateDirectory(Path.Combine(root.Path, "exe")).FullName;
        string fallbackRoot = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var store = new SettingsStore(exe, fallbackRoot);
        Assert.True(store.Save(Values(dark: true, fontSize: 14)).Success);
        byte[] original = File.ReadAllBytes(store.FilePath);
        using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var saved = store.Save(Values(dark: false, fontSize: 20));
            Assert.False(saved.Success);
            Assert.DoesNotContain("\\", saved.Message);
            Assert.DoesNotContain("/", saved.Message);
            Assert.DoesNotContain(exe, saved.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(original, File.ReadAllBytes(store.FilePath));
        Assert.False(File.Exists(store.FallbackFilePath));
        Assert.Equal(Values(dark: true, fontSize: 14), new SettingsStore(exe, fallbackRoot).Load().Settings);
    }

    [Fact]
    public void ReadFailureOnEitherFileReturnsAPathFreeMessage()
    {
        using var root = new SyntheticRoot();
        string exe = Directory.CreateDirectory(Path.Combine(root.Path, "exe")).FullName;
        string fallbackRoot = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var primary = new SettingsStore(exe, fallbackRoot);
        Assert.True(primary.Save(Values(dark: false, fontSize: 18)).Success);
        using (var locked = new FileStream(primary.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var loaded = primary.Load();
            Assert.False(loaded.Success);
            Assert.False(loaded.CanSave);
            AssertNoPath(loaded.Message, root.Path);
        }

        string emptyExe = Directory.CreateDirectory(Path.Combine(root.Path, "empty-exe")).FullName;
        var fallback = new SettingsStore(emptyExe, fallbackRoot);
        Assert.NotNull(fallback.FallbackFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fallback.FallbackFilePath)!);
        File.Copy(primary.FilePath, fallback.FallbackFilePath, overwrite: true);
        using (var locked = new FileStream(fallback.FallbackFilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var loaded = fallback.Load();
            Assert.False(loaded.Success);
            Assert.False(loaded.CanSave);
            AssertNoPath(loaded.Message, root.Path);
        }
    }

    [Fact]
    public void ExplicitSettingsFolderDoesNotUseLocalApplicationData()
    {
        using var root = new SyntheticRoot();
        var store = new SettingsStore(root.Path);
        Assert.Null(store.FallbackFilePath);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            string realFallback = Path.GetFullPath(Path.Combine(local, "ThreadLogViewer", "ui-settings.json"));
            Assert.NotEqual(realFallback, Path.GetFullPath(store.FilePath));
        }

        Assert.True(store.Save(Values(dark: false, fontSize: 16)).Success);
        Assert.True(File.Exists(store.FilePath));
        Assert.Equal(Values(dark: false, fontSize: 16), store.Load().Settings);
    }

    [Fact]
    public void ReadOnlyExeFileFailsWithoutCreatingTheFallback()
    {
        using var root = new SyntheticRoot();
        string exe = Directory.CreateDirectory(Path.Combine(root.Path, "exe")).FullName;
        string fallbackRoot = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var store = new SettingsStore(exe, fallbackRoot);
        Assert.True(store.Save(Values(dark: true, fontSize: 14)).Success);
        byte[] original = File.ReadAllBytes(store.FilePath);
        File.SetAttributes(store.FilePath, File.GetAttributes(store.FilePath) | FileAttributes.ReadOnly);
        try
        {
            var saved = store.Save(Values(dark: false, fontSize: 20));
            Assert.False(saved.Success);
            Assert.Equal("설정을 저장하지 못했습니다. 현재 화면 설정은 유지됩니다.", saved.Message);
            Assert.DoesNotContain("\\", saved.Message);
            Assert.DoesNotContain(":\\", saved.Message);
            Assert.DoesNotContain("AppData", saved.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("user-data", saved.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(root.Path, saved.Message, StringComparison.OrdinalIgnoreCase);
            string user = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(user)) Assert.DoesNotContain(user, saved.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(original, File.ReadAllBytes(store.FilePath));
            Assert.NotNull(store.FallbackFilePath);
            Assert.False(File.Exists(store.FallbackFilePath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(store.FallbackFilePath)));
        }
        finally
        {
            File.SetAttributes(store.FilePath, File.GetAttributes(store.FilePath) & ~FileAttributes.ReadOnly);
        }
    }

    [Fact]
    public void CorruptedFallbackFileSetsCanSaveFalse()
    {
        using var root = new SyntheticRoot();
        string exe = Directory.CreateDirectory(Path.Combine(root.Path, "exe")).FullName;
        string fallbackRoot = Directory.CreateDirectory(Path.Combine(root.Path, "fallback")).FullName;
        var store = new SettingsStore(exe, fallbackRoot);
        Assert.NotNull(store.FallbackFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FallbackFilePath)!);
        File.WriteAllText(store.FallbackFilePath, "{broken synthetic JSON");
        var loaded = store.Load();
        Assert.False(loaded.Success);
        Assert.False(loaded.CanSave);
        AssertNoPath(loaded.Message, root.Path);
        Assert.Equal("{broken synthetic JSON", File.ReadAllText(store.FallbackFilePath));
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public Task ThreeFailedRetriesStopSchedulingAnotherSave() => InSta(() =>
    {
        using var root = new SyntheticRoot();
        var store = new SettingsStore(root.Path);
        Assert.Null(store.FallbackFilePath);
        Assert.True(store.Save(UiSettings.Default).Success);
        var window = new MainWindow(root.Path, true);
        try
        {
            Assert.Null(Field<SettingsStore>(window, "settingsStore").FallbackFilePath);
            File.SetAttributes(store.FilePath, File.GetAttributes(store.FilePath) | FileAttributes.ReadOnly);
            Invoke(window, "ScheduleSettingsSave");
            var timer = Field<DispatcherTimer>(window, "settingsTimer");
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Invoke(window, "SaveSettingsNow");
                Assert.Equal(attempt, Field<int>(window, "settingsSaveFailures"));
                Assert.True(Field<bool>(window, "settingsDirty"));
                Assert.True(timer.IsEnabled);
                Assert.Equal(TimeSpan.FromSeconds(Math.Min(8, Math.Pow(2, attempt))), timer.Interval);
                Assert.Contains("저장 안 됨", Control<TextBlock>(window, "OperationStatus").Text);
                Assert.DoesNotContain("\\", Control<TextBlock>(window, "OperationStatus").Text);
                Assert.DoesNotContain(root.Path, Control<TextBlock>(window, "OperationStatus").Text, StringComparison.OrdinalIgnoreCase);
            }

            Invoke(window, "SaveSettingsNow");
            Assert.Equal(4, Field<int>(window, "settingsSaveFailures"));
            Assert.False(timer.IsEnabled);
            Assert.True(Field<bool>(window, "settingsDirty"));
        }
        finally
        {
            try
            {
                File.SetAttributes(store.FilePath, File.GetAttributes(store.FilePath) & ~FileAttributes.ReadOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            window.Close();
        }

        return Task.CompletedTask;
    });

    private static void AssertNoPath(string message, string folder)
    {
        Assert.Equal("설정을 읽지 못했습니다. 기존 파일을 보존합니다.", message);
        Assert.DoesNotContain("\\", message);
        Assert.DoesNotContain(":\\", message);
        Assert.DoesNotContain("AppData", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user-data", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(folder, message, StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (!string.IsNullOrWhiteSpace(user)) Assert.DoesNotContain(user, message, StringComparison.OrdinalIgnoreCase);
    }

    private static Task InSta(Func<Task> action) =>
        (Task)typeof(WindowFeatureTests).GetMethod("InSta", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [action])!;

    private static T Field<T>(MainWindow window, string name) =>
        (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static object? Invoke(MainWindow window, string name, params object?[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);

    private static T Control<T>(MainWindow window, string name) where T : class => (T)window.FindName(name);

    private static UiSettings Values(bool dark, double fontSize) => new()
    {
        Dark = dark,
        Compact = true,
        WordWrap = true,
        FontSize = fontSize,
        PanelWidth = 420,
        ResultsHeight = 360,
        ResultsCollapsed = true,
        ThreadBackgrounds = false,
        LargeLinePreview = false
    };

    private static void AssertUnwritable(string directory)
    {
        string probe = Path.Combine(directory, "synthetic-write-probe.txt");
        try
        {
            File.WriteAllText(probe, "synthetic");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return;
        }
        finally
        {
            try
            {
                if (File.Exists(probe)) File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        throw new Xunit.Sdk.XunitException("폴더에 여전히 쓸 수 있습니다.");
    }

    private sealed class DenyWrite : IDisposable
    {
        private readonly string path;
        private readonly string sid;
        private bool disposed;

        public DenyWrite(string path)
        {
            this.path = path;
            sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("현재 사용자 SID가 없습니다.");
            RunIcacls(path, "/deny", $"*{sid}:(OI)(CI)W");
            try
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { RunIcacls(path, "/remove:d", $"*{sid}"); }
            catch (InvalidOperationException) { }
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static void RunIcacls(string path, string action, string identity)
        {
            var start = new ProcessStartInfo("icacls")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(path);
            start.ArgumentList.Add(action);
            start.ArgumentList.Add(identity);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("icacls를 시작하지 못했습니다.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"icacls가 {process.ExitCode}로 끝났습니다. {output} {error}");
        }
    }

    private sealed class SyntheticRoot : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "synthetic-settings-fallback", Guid.NewGuid().ToString("N"));

        public SyntheticRoot() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
