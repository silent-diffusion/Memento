namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// 2.5.0: "Your own server" in the settings snapshot. The key itself never crosses the bridge; where the server runs is
/// worked out from the address alone (no lookup).
/// </summary>
/// <param name="HasKey">A bearer key is saved for the server (optional; most servers on this PC need none).</param>
public sealed record AiServerState(bool HasKey)
{
    /// <summary>The address as saved (<c>http://localhost:11434</c>), or <c>null</c> when none is set.</summary>
    public string? Address { get; set; }

    /// <summary>The model the server is asked for (<c>qwen3:8b</c>), or <c>null</c> when none is chosen.</summary>
    public string? Model { get; set; }

    /// <summary>The context the server runs the model with (default 8,192).</summary>
    public int ContextTokens { get; set; }

    /// <summary>The address is this PC (<c>localhost</c>, 127.0.0.0/8, [::1]).</summary>
    public bool Loopback { get; set; }

    /// <summary>Sending to the address takes the inputs off this PC (any address but a loopback one): "Allow external AI services" and the send confirmation apply.</summary>
    public bool LeavesPc { get; set; }

    /// <summary>A plain <c>http://</c> address outside this PC and the local network: refused until it is <c>https://</c>.</summary>
    public bool Insecure { get; set; }
}
