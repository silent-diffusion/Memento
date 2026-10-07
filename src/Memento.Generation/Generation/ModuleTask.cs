using Memento.Documents.Model.Modules;
using Memento.Documents.Templates;

namespace Memento.Generation.Generation;

/// <summary>
/// One map pass over the chunks. Modules that need the same extraction share it: Decisions, Action items, Owner,
/// Deadline and the Follow-up email read one decisions-and-actions pass (the extraction verified in the October 2026
/// spike); every summary-like module has its own pass with its own instructions.
/// </summary>
/// <param name="Family"><c>commitments</c>, <c>agenda</c>, <c>quotes</c>, <c>next</c>, or <c>points:&lt;module id&gt;</c>.</param>
/// <param name="Modules">The template modules that read its claims.</param>
public sealed record ModuleTask(string Family, IReadOnlyList<TemplateModule> Modules)
{
    public const string Commitments = "commitments";
    public const string AgendaCoverage = "agenda";
    public const string Quotes = "quotes";
    public const string NextMeeting = "next";
    public const string PointsPrefix = "points:";

    public bool IsPoints => Family.StartsWith(PointsPrefix, StringComparison.Ordinal);

    /// <summary>The module type a points pass writes (<c>executiveSummary</c>).</summary>
    public string? PointsType => IsPoints ? Modules[0].Type : null;

    /// <summary>Points asked for per chunk: the meeting purpose is one line, so one; otherwise by length.</summary>
    public int PointsPerChunk => PointsType == ModuleIds.MeetingPurpose ? 1 : PerChunk(Length);

    /// <summary>The longest length any reading module asks for.</summary>
    public ModuleLength Length => Modules.Select(m => m.Length).OrderByDescending(Rank).First();

    /// <summary>The modules' own instructions, which constrain tone and length but never the grounding rules.</summary>
    public string Instructions => string.Join(
        "\n",
        Modules.Select(m => m.Instructions?.Trim()).Where(i => !string.IsNullOrEmpty(i)).Distinct(StringComparer.Ordinal));

    /// <summary>The family a module type is extracted with, or <c>null</c> for modules that are not map passes.</summary>
    public static string? FamilyOf(TemplateModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        return module.Type switch
        {
            ModuleIds.Decisions or ModuleIds.ActionItems or ModuleIds.Owner or ModuleIds.Deadline or ModuleIds.FollowUpEmail => Commitments,
            ModuleIds.Agenda => AgendaCoverage,
            ModuleIds.Quote => Quotes,
            ModuleIds.NextMeeting => NextMeeting,
            ModuleIds.Summary or ModuleIds.ExecutiveSummary or ModuleIds.Discussion or ModuleIds.Topic or ModuleIds.CustomAi
                or ModuleIds.OpenQuestions or ModuleIds.Timeline or ModuleIds.MeetingPurpose => PointsPrefix + module.Id,
            _ => null,
        };
    }

    /// <summary>Points kept per module after reduction: short 3, medium 6, long 12 (decisions and actions are not capped below 20).</summary>
    public static int Cap(ModuleLength length) => length switch
    {
        ModuleLength.Short => 3,
        ModuleLength.Long => 12,
        _ => 6,
    };

    /// <summary>Points asked for per chunk.</summary>
    public static int PerChunk(ModuleLength length) => length switch
    {
        ModuleLength.Short => 3,
        ModuleLength.Long => 8,
        _ => 5,
    };

    private static int Rank(ModuleLength length) => length switch
    {
        ModuleLength.Short => 0,
        ModuleLength.Long => 2,
        _ => 1,
    };
}
