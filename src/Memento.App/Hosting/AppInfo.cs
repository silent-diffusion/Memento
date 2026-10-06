using System.Reflection;
using Memento.Core.Host;

namespace Memento.App.Hosting;

/// <summary><see cref="IAppInfo"/> from the entry assembly and the OS.</summary>
internal sealed class AppInfo : IAppInfo
{
    public string Version { get; } = ReadProductVersion();

    public string OsVersion { get; } = DescribeWindows(Environment.OSVersion.Version);

    /// <summary>The SemVer from Directory.Build.props, without the source-revision suffix the SDK appends.</summary>
    public static string ReadProductVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }

    private static string DescribeWindows(Version version) => $"Windows {version.Major}.{version.Minor}.{version.Build}";
}
