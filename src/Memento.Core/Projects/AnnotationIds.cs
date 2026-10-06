using System.Security.Cryptography;

namespace Memento.Core.Projects;

/// <summary>Short opaque ids for chapters (<c>c…</c>), highlights (<c>h…</c>) and topics (<c>t…</c>).</summary>
public static class AnnotationIds
{
    public static string New(char prefix) => prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
}
