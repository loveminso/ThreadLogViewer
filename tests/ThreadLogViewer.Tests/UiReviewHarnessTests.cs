using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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

    [Fact]
    public void InactiveReviewChangesNothing()
    {
        foreach (string? review in new string?[] { null, "", "0", "true", "150" })
        {
            UiReviewHarness.ReviewEffects effects = UiReviewHarness.Effects(review, "150");
            Assert.False(effects.Active);
            Assert.False(effects.SkipMonitorClamp);
            Assert.False(effects.SoftwareRender);
            Assert.Equal(1.0, effects.ContentScale);
            Assert.False(UiReviewHarness.SkipMonitorClamp(review));
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window { Content = new Grid() };
                var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                Assert.Same(Transform.Identity, content.LayoutTransform);
                RenderMode renderMode = RenderOptions.ProcessRenderMode;
                UiReviewHarness.Apply(window, review: null, scale: "150");
                UiReviewHarness.Apply(window, review: "150", scale: "150");
                Assert.Same(Transform.Identity, content.LayoutTransform);
                Assert.Equal(renderMode, RenderOptions.ProcessRenderMode);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA review harness test timed out");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void ActiveReviewRequestsTheClampSkipAndContentScale()
    {
        UiReviewHarness.ReviewEffects fullSize = UiReviewHarness.Effects("1", null);
        Assert.True(fullSize.Active);
        Assert.True(fullSize.SkipMonitorClamp);
        Assert.True(fullSize.SoftwareRender);
        Assert.Equal(1.0, fullSize.ContentScale);
        Assert.True(UiReviewHarness.SkipMonitorClamp("1"));

        UiReviewHarness.ReviewEffects scaled = UiReviewHarness.Effects("1", "150");
        Assert.Equal(1.5, scaled.ContentScale);
        Assert.True(scaled.SkipMonitorClamp);
        Assert.True(scaled.SoftwareRender);
    }
}
