using Microsoft.Win32;

namespace Memento.Core.Maintenance;

/// <summary>
/// <see cref="IStartupRegistration"/> through the current user's <c>Run</c> key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, value <c>Memento</c>); no admin rights needed.
/// The value is removed, not emptied, when startup is turned off.
/// </summary>
public sealed class RegistryStartupRegistration : Host.IStartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Memento";

    private readonly string _keyPath;
    private readonly string _valueName;
    private readonly Func<string?> _executable;

    public RegistryStartupRegistration()
        : this(RunKeyPath, ValueName, () => Environment.ProcessPath)
    {
    }

    /// <param name="keyPath">Below HKEY_CURRENT_USER; tests use a key of their own.</param>
    public RegistryStartupRegistration(string keyPath, string valueName, Func<string?> executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentNullException.ThrowIfNull(executable);
        _keyPath = keyPath;
        _valueName = valueName;
        _executable = executable;
    }

    public bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: false);
            return key?.GetValue(_valueName) is string value && value.Length > 0;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Starting with Windows needs Windows.");
        }

        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true);
        if (enabled)
        {
            var executable = _executable() ?? throw new IOException("Memento could not find its own program file to register.");
            key.SetValue(_valueName, $"\"{executable}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}
