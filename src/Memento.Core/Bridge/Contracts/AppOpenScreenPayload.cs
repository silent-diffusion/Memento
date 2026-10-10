namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Payload of <c>app.openScreen</c> (2.0): the tray's Record item asks the page to open the Recording session (the
/// active one when a recording is in progress, else a new one ready to record).
/// </summary>
/// <param name="Screen"><c>record</c>; the only screen the host opens.</param>
public sealed record AppOpenScreenPayload(string Screen)
{
    public const string Record = "record";
}
