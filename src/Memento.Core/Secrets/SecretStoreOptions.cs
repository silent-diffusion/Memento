namespace Memento.Core.Secrets;

/// <summary>Where the encrypted keys live: <c>%LOCALAPPDATA%\Memento\secrets.bin</c> by default.</summary>
public sealed record SecretStoreOptions(string FilePath)
{
    public const string FileName = "secrets.bin";

    public static SecretStoreOptions Default => new(Path.Combine(AppPaths.DataRoot, FileName));
}
