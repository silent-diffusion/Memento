using System.Globalization;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Templates;

namespace Memento.Generation.Generation;

/// <summary>
/// The Follow-up email module (2.0): an email ready to send to the people who were there. Who it is to, the subject,
/// the greeting, the opening and the closing are written in code from the recording's details and the module's tone
/// and sign-off; the recap, the decisions and the action items with their owners are the verified claims, so the email
/// says nothing the transcript does not. Nothing in it is sent anywhere: it is copied or exported by the person.
/// </summary>
public static class FollowUpEmailWriter
{
    public const string ToLabel = "To";
    public const string SubjectLabel = "Subject";
    public const string NoRecipients = "No participants were listed. Add the recipients when you send it.";
    public const string NothingAgreed = "No decisions or action items were recorded.";

    /// <summary>A greeting names people when there are at most this many.</summary>
    public const int MaxGreetingNames = 4;

    /// <param name="claims">The module's kept claims in time order: recap points, decisions and action items.</param>
    /// <param name="stamp">The timestamp runs after a statement when the module links to the transcript.</param>
    public static List<Block> Write(IReadOnlyList<Claim> claims, GenerationFacts facts, EmailOptions? options, Func<Claim, IEnumerable<Run>> stamp)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(stamp);
        var email = options ?? EmailOptions.Default;
        var recap = claims.Where(c => c.Kind == ClaimKinds.Point).ToList();
        var decisions = claims.Where(c => c.Kind == ClaimKinds.Decision).ToList();
        var actions = claims.Where(c => c.Kind == ClaimKinds.Action).ToList();
        var people = facts.People.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var title = string.IsNullOrWhiteSpace(facts.Title) ? null : facts.Title.Trim();

        var blocks = new List<Block>
        {
            new LabelValueBlock
            {
                Pairs =
                [
                    new LabelValuePair { Label = ToLabel, Runs = people.Count == 0 ? [Run.Note(NoRecipients)] : [Run.Plain(string.Join("; ", people))] },
                    new LabelValuePair { Label = SubjectLabel, Runs = [Run.Plain(Subject(email.Tone, title, facts.RecordedAt))] },
                ],
            },
            Paragraph(Greeting(email.Tone, people)),
            Paragraph(Opening(email.Tone, title, facts.RecordedAt)),
        };

        if (recap.Count > 0)
        {
            if (email.Tone != EmailOptions.Brief)
            {
                blocks.Add(new ParagraphBlock { Runs = [Run.Bold("Summary")] });
            }

            blocks.Add(new ParagraphBlock { Runs = recap.SelectMany((c, i) => (IEnumerable<Run>)[.. (i > 0 ? new[] { Run.Plain(" ") } : []), Run.Plain(ModuleWriter.Sentence(c.Text)), .. stamp(c)]).ToList() });
        }

        if (decisions.Count > 0)
        {
            blocks.Add(new ParagraphBlock { Runs = [Run.Bold("Decisions")] });
            blocks.Add(new ListBlock { Items = decisions.Select(c => new ListItem { Runs = [Run.Plain(ModuleWriter.Sentence(c.Text)), .. stamp(c)] }).ToList() });
        }

        if (actions.Count > 0)
        {
            blocks.Add(new ParagraphBlock { Runs = [Run.Bold("Action items")] });
            blocks.Add(new ListBlock { Items = actions.Select(c => new ListItem { Runs = [.. Action(c), .. stamp(c)] }).ToList() });
        }

        if (decisions.Count == 0 && actions.Count == 0)
        {
            blocks.Add(ModuleWriter.Note(NothingAgreed));
        }

        if (Closing(email.Tone) is { } closing)
        {
            blocks.Add(Paragraph(closing));
        }

        blocks.Add(Paragraph(SignOffWords(email.Tone)));
        if (!string.IsNullOrWhiteSpace(email.SignOff))
        {
            blocks.Add(Paragraph(email.SignOff.Trim()));
        }

        return blocks;
    }

    /// <summary>"Follow-up: Design review (5 October 2026)"; brief: "Recap: Design review".</summary>
    public static string Subject(string tone, string? title, DateTimeOffset? recordedAt)
    {
        var what = title ?? "our meeting";
        if (tone == EmailOptions.Brief)
        {
            return $"Recap: {what}";
        }

        return recordedAt is { } at ? $"Follow-up: {what} ({MetaLine.ShortDate(at)})" : $"Follow-up: {what}";
    }

    /// <summary>"Hello Priya, Sam and Lee," with up to four first names, else "Hello everyone,"; warm "Hi …", brief "Hi all,".</summary>
    public static string Greeting(string tone, IReadOnlyList<string> people)
    {
        ArgumentNullException.ThrowIfNull(people);
        var hello = tone == EmailOptions.Neutral ? "Hello" : "Hi";
        if (people.Count == 0 || people.Count > MaxGreetingNames || (tone == EmailOptions.Brief && people.Count > 1))
        {
            return tone == EmailOptions.Brief ? "Hi all," : $"{hello} everyone,";
        }

        var first = people.Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]).ToList();
        var names = first.Count == 1 ? first[0] : string.Join(", ", first.Take(first.Count - 1)) + " and " + first[^1];
        return $"{hello} {names},";
    }

    public static string Opening(string tone, string? title, DateTimeOffset? recordedAt)
    {
        var what = title ?? "the meeting";
        var when = recordedAt is { } at ? " on " + at.ToString("dddd d MMMM", CultureInfo.InvariantCulture) : string.Empty;
        return tone switch
        {
            EmailOptions.Warm => $"Thank you all for your time at {what}{when}. Here is a short recap of what we covered and agreed.",
            EmailOptions.Brief => $"A quick recap of {what}.",
            _ => $"Thank you for joining {what}{when}. Here is a summary of what we discussed and agreed.",
        };
    }

    public static string? Closing(string tone) => tone switch
    {
        EmailOptions.Warm => "Thanks again, everyone. Let me know if I missed anything.",
        EmailOptions.Brief => null,
        _ => "Please reply if anything here does not match your notes.",
    };

    public static string SignOffWords(string tone) => tone switch
    {
        EmailOptions.Warm => "Warm regards,",
        EmailOptions.Brief => "Thanks,",
        _ => "Best regards,",
    };

    /// <summary>"Sam: Send the revised budget, by Friday"; without an owner, "Send the revised budget (no owner named)".</summary>
    private static IEnumerable<Run> Action(Claim claim)
    {
        var task = ModuleWriter.Sentence(claim.Text).TrimEnd('.');
        var due = claim.Due is { } d ? $", {d}" : string.Empty;
        return claim.Owner is { } owner
            ? [Run.Bold(owner + ":"), Run.Plain($" {task}{due}.")]
            : [Run.Plain($"{task}{due} "), Run.Note("(" + ModuleWriter.NoOwner.ToLowerInvariant() + ")"), Run.Plain(".")];
    }

    private static ParagraphBlock Paragraph(string text) => new() { Runs = [Run.Plain(text)] };
}
