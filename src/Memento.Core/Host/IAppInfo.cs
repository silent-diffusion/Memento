namespace Memento.Core.Host;

/// <summary>Facts about the running build and machine, provided by the host.</summary>
public interface IAppInfo
{
    /// <summary>SemVer product version, e.g. <c>0.1.0</c>.</summary>
    string Version { get; }

    /// <summary>Human-readable Windows version, e.g. <c>Windows 10.0.26200</c>.</summary>
    string OsVersion { get; }
}
