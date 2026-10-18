using System.Windows;
using System.Windows.Interop;
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

    /// <summary>The monitor clamp is skipped only when the review flag is exactly "1".</summary>
    public static bool SkipMonitorClamp(string? review) => IsActive(review);

    /// <summary>150 means a content scale of 1.5. Any other value, including null, is 100%.</summary>
    public static double ContentScale(string? scale) =>
        string.Equals(scale, "150", StringComparison.Ordinal) ? 1.5 : 1.0;

    /// <summary>Inactive review leaves render mode, content scale, and the monitor clamp unchanged.</summary>
    public static ReviewEffects Effects(string? review, string? scale) =>
        IsActive(review)
            ? new ReviewEffects(true, true, true, ContentScale(scale))
            : new ReviewEffects(false, false, false, 1.0);

    public static void Apply(Window window) =>
        Apply(window, Environment.GetEnvironmentVariable(ReviewVariable), Environment.GetEnvironmentVariable(ScaleVariable));

    public static void Apply(Window window, string? review, string? scale)
    {
        ReviewEffects effects = Effects(review, scale);
        if (!effects.Active)
            return;
        // PrintWindow records a black client area for the default hardware render target.
        // Only the review process switches to software rendering.
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        if (effects.ContentScale == 1.0 || window.Content is not FrameworkElement root)
            return;
        root.LayoutTransform = new ScaleTransform(effects.ContentScale, effects.ContentScale);
    }

    public readonly record struct ReviewEffects(bool Active, bool SkipMonitorClamp, bool SoftwareRender, double ContentScale);
}
