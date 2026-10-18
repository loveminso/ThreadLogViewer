using System.Windows;
using System.Windows.Media;

namespace ThreadLogViewer.App;

/// <summary>
/// Test-only switch for the CI screenshot gallery. Normal launches leave every value unset,
/// so layout, render mode, and the monitor clamp stay on the existing path.
/// </summary>
public static class UiReviewHarness
{
    public const string ReviewVariable = "THREADLOGVIEWER_UI_REVIEW";
    public const string ScaleVariable = "THREADLOGVIEWER_UI_REVIEW_SCALE";

    public static bool IsActive() => IsActive(Environment.GetEnvironmentVariable(ReviewVariable));

    public static bool IsActive(string? review) => string.Equals(review, "1", StringComparison.Ordinal);

    /// <summary>150 means a content scale of 1.5. Any other value, including null, is 100%.</summary>
    public static double ContentScale(string? scale) =>
        string.Equals(scale, "150", StringComparison.Ordinal) ? 1.5 : 1.0;

    public static void Apply(Window window)
    {
        if (!IsActive())
            return;
        // PrintWindow records a black client area for the default hardware render target.
        // Only the review process switches to software rendering.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        double scale = ContentScale(Environment.GetEnvironmentVariable(ScaleVariable));
        if (scale == 1.0 || window.Content is not FrameworkElement root)
            return;
        root.LayoutTransform = new ScaleTransform(scale, scale);
    }
}
