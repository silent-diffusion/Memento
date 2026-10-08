namespace Memento.AI;

/// <summary>
/// A request as the text its provider receives (the Live output view): the system prompt, then every turn, each under
/// its role, with the strings exactly as sent. The payload in them is the one "Preview exactly what will be sent" shows,
/// cut into the request's chunk; the hash in the generation record is of the same strings (<see cref="AiRequestHash"/>).
/// </summary>
public static class AiRequestText
{
    public const string SystemHeading = "System";
    public const string UserHeading = "User";
    public const string AssistantHeading = "Assistant";

    public static string Render(AiRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var parts = new List<string>(request.Messages.Count + 1);
        if (request.System.Length > 0)
        {
            parts.Add(SystemHeading + "\n" + request.System);
        }

        parts.AddRange(request.Messages.Select(m => (m.Role == AiRole.User ? UserHeading : AssistantHeading) + "\n" + m.Content));
        return string.Join("\n\n", parts);
    }
}
