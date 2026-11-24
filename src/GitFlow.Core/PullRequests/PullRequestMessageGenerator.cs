using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GitFlow.Core.Model;
using GitFlow.Core.Resources;

namespace GitFlow.Core.PullRequests;

/// <summary>Lines changed between target and source (<c>git diff --shortstat target...source</c>).</summary>
public sealed record DiffSummary(int Files, int Insertions, int Deletions)
{
    public static DiffSummary Empty { get; } = new(0, 0, 0);

    /// <summary>Parses git's English shortstat (LC_ALL=C), e.g. " 5 files changed, 120 insertions(+), 8 deletions(-)".</summary>
    public static DiffSummary Parse(string? shortstat)
    {
        static int Read(string text, string word)
        {
            var match = Regex.Match(text, @"(\d+) " + word);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        var text = shortstat ?? string.Empty;
        return new DiffSummary(Read(text, "files? changed"), Read(text, @"insertions?\(\+\)"), Read(text, @"deletions?\(-\)"));
    }
}

/// <summary>Everything the PR message is built from (git data only).</summary>
public sealed record PullRequestContext(
    FlowBranch Branch,
    string Target,
    IReadOnlyList<string> CommitSubjects,
    string CommitMessagesText,
    DiffSummary Diff,
    string? TagName);

/// <summary>Title + description of a PR, editable by the user before creation.</summary>
public sealed record PullRequestDraft(string Source, string Target, string Title, string Description);

/// <summary>
/// Builds the PR title and description deterministically from git (no AI).
/// Optional template with variables {type} {source} {target} {commits} {stats} {version} {tickets};
/// a platform template without variables is used as the base, the generated description is appended.
/// </summary>
public static class PullRequestMessageGenerator
{
    public const string DefaultTicketPattern = @"(?<![A-Za-z0-9])([A-Z][A-Z0-9]+-\d+|#\d+)(?![0-9])";

    private static readonly string[] Variables = ["{type}", "{source}", "{target}", "{commits}", "{stats}", "{version}", "{tickets}"];

    public static string Title(FlowBranch branch)
    {
        var name = branch.Name.Replace('-', ' ').Replace('_', ' ').Trim();
        return branch.Type switch
        {
            BranchType.Feature => $"Feature: {name}",
            BranchType.Release => $"Release {branch.Name}",
            _ => $"Hotfix {branch.Name}",
        };
    }

    public static PullRequestDraft Build(PullRequestContext context, string? template = null, string? ticketPattern = null)
    {
        var values = new Dictionary<string, string>
        {
            ["{type}"] = TypeLabel(context.Branch.Type),
            ["{source}"] = context.Branch.FullName,
            ["{target}"] = context.Target,
            ["{commits}"] = Commits(context.CommitSubjects),
            ["{stats}"] = Stats(context.Diff),
            ["{version}"] = context.TagName is null ? string.Empty : context.Branch.Name,
            ["{tickets}"] = string.Join(", ", FindTickets(context, ticketPattern)),
        };

        string description;
        if (template is not null && Variables.Any(v => template.Contains(v, StringComparison.Ordinal)))
        {
            description = values.Aggregate(template, (text, pair) => text.Replace(pair.Key, pair.Value, StringComparison.Ordinal));
        }
        else
        {
            description = Default(context, values);
            if (!string.IsNullOrWhiteSpace(template))
            {
                description = template.TrimEnd() + Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine + description;
            }
        }

        return new PullRequestDraft(context.Branch.FullName, context.Target, Title(context.Branch), description.Trim());
    }

    public static IReadOnlyList<string> FindTickets(PullRequestContext context, string? pattern = null)
    {
        Regex regex;
        try
        {
            regex = new Regex(string.IsNullOrWhiteSpace(pattern) ? DefaultTicketPattern : pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            regex = new Regex(DefaultTicketPattern);
        }

        return regex.Matches(context.Branch.FullName + "\n" + context.CommitMessagesText)
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Default(PullRequestContext context, IReadOnlyDictionary<string, string> values)
    {
        var nl = Environment.NewLine;
        var text = new StringBuilder();
        text.Append(FlowMessages.Format("Pr_Header", values["{type}"], context.Branch.FullName, context.Target)).Append(nl).Append(nl);
        text.Append("### ").Append(FlowMessages.Format("Pr_Commits", context.CommitSubjects.Count)).Append(nl);
        text.Append(values["{commits}"]).Append(nl).Append(nl);
        text.Append("### ").Append(FlowMessages.Get("Pr_Files")).Append(nl);
        text.Append(values["{stats}"]).Append(nl);

        if (context.TagName is not null)
        {
            text.Append(nl).Append("### ").Append(FlowMessages.Get("Pr_Version")).Append(nl);
            text.Append(FlowMessages.Format("Pr_VersionText", context.Branch.Name, context.TagName)).Append(nl);
        }

        if (values["{tickets}"].Length > 0)
        {
            text.Append(nl).Append("### ").Append(FlowMessages.Get("Pr_Tickets")).Append(nl);
            text.Append(values["{tickets}"]).Append(nl);
        }

        return text.ToString();
    }

    private static string Commits(IReadOnlyList<string> subjects) =>
        subjects.Count == 0
            ? FlowMessages.Get("Pr_NoCommit")
            : string.Join(Environment.NewLine, subjects.Select(s => "- " + s));

    private static string Stats(DiffSummary diff) =>
        FlowMessages.Format("Pr_Stats", diff.Files, diff.Insertions, diff.Deletions);

    private static string TypeLabel(BranchType type) => type switch
    {
        BranchType.Feature => "Feature",
        BranchType.Release => "Release",
        _ => "Hotfix",
    };
}
