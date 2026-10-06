namespace Memento.Core.Bridge.Contracts;

/// <summary>Free space on the drive that holds the library.</summary>
/// <param name="FreeBytes">Free bytes available to the user, or <c>null</c> when the drive cannot be read.</param>
/// <param name="LowSpace"><c>true</c> below the low-space threshold (10 GB by default).</param>
public sealed record StorageStatus(long? FreeBytes, bool LowSpace);
