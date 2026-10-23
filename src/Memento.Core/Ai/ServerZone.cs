namespace Memento.Core.Ai;

/// <summary>
/// Where "Your own server" runs, judged from its address alone (no lookup): on this PC, on the local network, or
/// anywhere else. Only <see cref="ThisPc"/> keeps the inputs on the PC; the other two need "Allow external AI services".
/// </summary>
public enum ServerZone
{
    /// <summary><c>localhost</c>, 127.0.0.0/8 or [::1]: nothing leaves the PC.</summary>
    ThisPc,

    /// <summary>RFC 1918, link-local, fc00::/7, a <c>.local</c> name or a single-label host name: the inputs leave the PC for the local network.</summary>
    Network,

    /// <summary>Any other address: the inputs leave the PC and may cross the internet, so only <c>https://</c> is accepted.</summary>
    Remote,
}
