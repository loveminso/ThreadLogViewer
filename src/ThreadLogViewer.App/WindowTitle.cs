using System.Globalization;
using System.Reflection;

namespace ThreadLogViewer.App;

/// <summary>
/// Window title text from the assembly version. <see cref="AssemblyName.Version"/> is
/// major.minor.build.revision (0.8.3.0 when <c>Directory.Build.props</c> Version is 0.8.3).
/// The revision and any InformationalVersion <c>+sha</c> suffix are not shown.
/// </summary>
public static class WindowTitle
{
    public static string Format(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        if (version.Build < 0)
            throw new ArgumentOutOfRangeException(nameof(version), "The window title needs major.minor.build.");
        return string.Concat(
            "ThreadLog Viewer v",
            version.Major.ToString(CultureInfo.InvariantCulture),
            ".",
            version.Minor.ToString(CultureInfo.InvariantCulture),
            ".",
            version.Build.ToString(CultureInfo.InvariantCulture));
    }

    public static string From(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Version? version = assembly.GetName().Version;
        if (version is null)
            throw new InvalidOperationException("Assembly version is missing.");
        return Format(version);
    }

    public static string WithSession(string sessionTitle, string appTitle)
    {
        ArgumentNullException.ThrowIfNull(sessionTitle);
        ArgumentNullException.ThrowIfNull(appTitle);
        return string.Concat(sessionTitle, " \u2014 ", appTitle);
    }
}
