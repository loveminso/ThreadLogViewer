using ThreadLogViewer.App;
using Xunit;

namespace ThreadLogViewer.Tests;

public sealed class OwnedDialogPlacementTests
{
    [Fact]
    public void FitCentersThePreferredSizeOnTheOwner()
    {
        var frame = OwnedDialogPlacement.Fit(100, 80, 800, 600, 0, 0, 1024, 768, 650, 500, 530, 390);
        Assert.Equal(175, frame.Left);
        Assert.Equal(130, frame.Top);
        Assert.Equal(650, frame.Width);
        Assert.Equal(500, frame.Height);
        Assert.Equal(530, frame.MinWidth);
        Assert.Equal(390, frame.MinHeight);
    }

    [Fact]
    public void FitClampsACenterThatWouldLeaveTheWorkArea()
    {
        var frame = OwnedDialogPlacement.Fit(900, 700, 400, 300, 0, 0, 1024, 768, 650, 500, 530, 390);
        Assert.Equal(374, frame.Left);
        Assert.Equal(268, frame.Top);
        Assert.Equal(650, frame.Width);
        Assert.Equal(500, frame.Height);
        Assert.True(frame.Left + frame.Width <= 1024);
        Assert.True(frame.Top + frame.Height <= 768);
    }

    [Fact]
    public void FitShrinksToTheWorkAreaAndDoesNotLetMinimumExceedThatSize()
    {
        var frame = OwnedDialogPlacement.Fit(0, 0, 400, 300, 10, 20, 480, 320, 650, 500, 530, 390);
        Assert.Equal(10, frame.Left);
        Assert.Equal(20, frame.Top);
        Assert.Equal(480, frame.Width);
        Assert.Equal(320, frame.Height);
        Assert.Equal(480, frame.MinWidth);
        Assert.Equal(320, frame.MinHeight);
        Assert.True(frame.MinWidth <= frame.Width);
        Assert.True(frame.MinHeight <= frame.Height);
    }
}
