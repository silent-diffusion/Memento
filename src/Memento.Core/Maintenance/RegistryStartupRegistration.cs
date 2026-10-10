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
    private readonly string? _arguments;

    public RegistryStartupRegistration()
        : this(RunKeyPath, ValueName, () => Environment.ProcessPath)
    {
    }

    /// <param name="keyPath">Below HKEY_CURRENT_USER; tests use a key of their own.</param>
    /// <param name="arguments">Added after the quoted program (the app passes <c>--background</c>: start in the tray or minimised).</param>
    public RegistryStartupRegistration(string keyPath, string valueName, Func<string?> executable, string? arguments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentNullException.ThrowIfNull(executable);
        _keyPath = keyPath;
        _valueName = valueName;
        _executable = executable;
        _arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
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
            key.SetValue(_valueName, _arguments is null ? $"\"{executable}\"" : $"\"{executable}\" {_arguments}", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(_valueName, throwOnMissingValue: false);
        }
    }
}
