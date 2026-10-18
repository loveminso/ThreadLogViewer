using ThreadLogViewer.App;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class UiReviewHarnessTests
{
    [Fact]
    public void ReviewModeStaysOffUnlessTheVariableIsExactlyOne()
    {
        Assert.False(UiReviewHarness.IsActive(null));
        Assert.False(UiReviewHarness.IsActive(""));
        Assert.False(UiReviewHarness.IsActive("true"));
        Assert.False(UiReviewHarness.IsActive("150"));
        Assert.False(UiReviewHarness.IsActive("1.0"));
        Assert.True(UiReviewHarness.IsActive("1"));
    }

    [Fact]
    public void ContentScaleIsIdentityUnlessTheHarnessRequests150()
    {
        Assert.Equal(1.0, UiReviewHarness.ContentScale(null));
        Assert.Equal(1.0, UiReviewHarness.ContentScale(""));
        Assert.Equal(1.0, UiReviewHarness.ContentScale("100"));
        Assert.Equal(1.0, UiReviewHarness.ContentScale("1.5"));
        Assert.Equal(1.5, UiReviewHarness.ContentScale("150"));
    }

    [Fact]
    public void ThisProcessIsNotTheReviewHarness()
    {
        Assert.False(UiReviewHarness.IsActive());
        Assert.Equal(1.0, UiReviewHarness.ContentScale(Environment.GetEnvironmentVariable(UiReviewHarness.ScaleVariable)));
    }
}
