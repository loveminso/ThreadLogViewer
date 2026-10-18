using System.Diagnostics;
using System.IO;
using System.Security.Principal;
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
