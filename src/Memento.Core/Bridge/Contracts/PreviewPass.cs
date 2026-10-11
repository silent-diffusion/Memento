namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// 2.0: one request in "Preview what will be sent", exactly as the provider will receive it (the Live output sheet's
/// request text). Map passes are complete; the verify pass is described, since its claims come from the answers.
/// </summary>
/// <param name="Id"><c>p1</c>, <c>p2</c>… in pipeline order.</param>
/// <param name="Step"><c>map</c> or <c>verify</c>.</param>
/// <param name="Title">"Decisions and action items · segment 1 of 2".</param>
/// <param name="System">The system prompt as sent.</param>
/// <param name="User">The user turn as sent: the sections this pass needs and its part of the transcript.</param>
/// <param name="Characters">Characters of the whole request (system and user).</param>
/// <param name="Bytes">UTF-8 bytes of the whole request.</param>
/// <param name="Tokens">About how many tokens the provider will count.</param>
/// <param name="Exact"><c>false</c> for the verify pass, whose claims and excerpts depend on the answers.</param>
public sealed record PreviewPass(string Id, string Step, string Title, string System, string User, long Characters, long Bytes, int Tokens, bool Exact);
