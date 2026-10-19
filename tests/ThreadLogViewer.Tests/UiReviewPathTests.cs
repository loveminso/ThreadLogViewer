using System.IO;
using ThreadLogViewer.UiTests;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class UiReviewPathTests
{
    [Theory]
    [InlineData(@"C:\Users\runneradmin\AppData\Local\Temp\tlv-ui-review-0123456789abcdef0123456789abcdef\review.log", @"<temp>\review.log")]
    [InlineData(@"C:/Users/alice/AppData/Local/Temp/tlv-ui-review-deadbeef/shot.png", "<temp>/shot.png")]
    [InlineData(@"c:\users\RUNNERADMIN\appdata\local\temp\tlv-ui-review-abc\note.txt", @"<temp>\note.txt")]
    [InlineData(@"C:\Users\runneradmin\AppData\Local\ThreadLogViewer\ui-settings.json", @"<temp>\Local\ThreadLogViewer\ui-settings.json")]
    [InlineData(@"D:\Users\builder\AppData\Roaming\ThreadLogViewer\cache", @"<temp>\Roaming\ThreadLogViewer\cache")]
    [InlineData(@"C:\Users\runneradmin\Desktop\note.txt", @"<temp>\Desktop\note.txt")]
    [InlineData(@"D:\a\ThreadLogViewer\ThreadLogViewer\TestResults\ui-review", "<temp>/ThreadLogViewer\\ThreadLogViewer\\TestResults\\ui-review")]
    [InlineData("D:/a/ThreadLogViewer/ThreadLogViewer/index.html", "<temp>/ThreadLogViewer/ThreadLogViewer/index.html")]
    [InlineData(@"<temp>\review.log", @"<temp>\review.log")]
    [InlineData("이미 가린 경로 <temp> 만 있다.", "이미 가린 경로 <temp> 만 있다.")]
    [InlineData("합성 로그만 사용합니다.", "합성 로그만 사용합니다.")]
    [InlineData("", "")]
    public void RedactReviewPaths_RewritesProfileTempAndRunnerCheckout(string input, string expected)
    {
        Assert.Equal(expected, UiReviewPaths.RedactReviewPaths(input));
    }

    [Fact]
    public void RejectRunnerPaths_CatchesCheckoutPathInIndexMarkdown()
    {
        using var dir = new ReviewOutputDirectory();
        dir.WriteAll(indexMarkdown: @"see D:\a\ThreadLogViewer\ThreadLogViewer\src");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UiReviewPaths.RejectRunnerPaths(dir.Path));

        Assert.Contains("index.md", ex.Message);
    }

    [Fact]
    public void RejectRunnerPaths_CatchesProfilePathInIndexHtml()
    {
        using var dir = new ReviewOutputDirectory();
        dir.WriteAll(indexHtml: @"<p>C:\Users\runneradmin\AppData\Local\Temp\tlv-ui-review-abc</p>");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UiReviewPaths.RejectRunnerPaths(dir.Path));

        Assert.Contains("index.html", ex.Message);
    }

    [Fact]
    public void RejectRunnerPaths_StillCatchesLintJson()
    {
        using var dir = new ReviewOutputDirectory();
        dir.WriteAll(lintJson: @"{""detail"":""C:\\Users\\runneradmin\\AppData""}");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => UiReviewPaths.RejectRunnerPaths(dir.Path));

        Assert.Contains("ui-lint.json", ex.Message);
    }

    [Fact]
    public void RejectRunnerPaths_AllowsRedactedIndexAndLintFiles()
    {
        using var dir = new ReviewOutputDirectory();
        dir.WriteAll(
            lintJson: @"{""detail"":""<temp>\\review.log""}",
            lintMarkdown: "path <temp>",
            indexMarkdown: "screenshot <temp>/review.log",
            indexHtml: "<p>&lt;temp&gt;/review.log</p>");

        UiReviewPaths.RejectRunnerPaths(dir.Path);
    }

    private sealed class ReviewOutputDirectory : IDisposable
    {
        internal ReviewOutputDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tlv-review-paths-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal void WriteAll(string? lintJson = null, string? lintMarkdown = null, string? indexMarkdown = null, string? indexHtml = null)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, "ui-lint.json"), lintJson ?? "{}\n");
            File.WriteAllText(System.IO.Path.Combine(Path, "ui-lint.md"), lintMarkdown ?? "# lint\n");
            File.WriteAllText(System.IO.Path.Combine(Path, "index.md"), indexMarkdown ?? "# index\n");
            File.WriteAllText(System.IO.Path.Combine(Path, "index.html"), indexHtml ?? "<p>index</p>\n");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                    Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
