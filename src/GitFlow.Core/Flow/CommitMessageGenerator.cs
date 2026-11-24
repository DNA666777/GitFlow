using System.Text;
using System.Text.RegularExpressions;
using GitFlow.Core.Model;
using GitFlow.Core.Resources;

namespace GitFlow.Core.Flow;

/// <summary>
/// Builds a commit message from git data only (deterministic, no AI): a summary line,
/// then the files grouped by kind of change with their +/- lines, then the ticket references
/// found in the branch name. Texts come from Resources/Strings.resx.
/// </summary>
public static partial class CommitMessageGenerator
{
    public const int MaxListedFiles = 50;
    private const int MaxSubjectLength = 72;

    private static readonly ChangeKind[] GroupOrder = [ChangeKind.Added, ChangeKind.Modified, ChangeKind.Renamed, ChangeKind.Deleted, ChangeKind.Conflicted];

    public static string Generate(IReadOnlyList<FileChange> changes, IReadOnlyDictionary<string, FileStat> stats, string? branchName)
    {
        if (changes.Count == 0)
        {
            return string.Empty;
        }

        var message = new StringBuilder(Subject(changes));
        message.AppendLine().AppendLine();

        var listed = 0;
        foreach (var kind in GroupOrder)
        {
            var group = changes.Where(c => c.Kind == kind).OrderBy(c => c.Path, StringComparer.Ordinal).ToArray();
            if (group.Length == 0)
            {
                continue;
            }

            message.AppendLine(FlowMessages.Format("Commit_Group_" + kind, group.Length));
            foreach (var change in group)
            {
                if (listed++ >= MaxListedFiles)
                {
                    break;
                }

                message.Append("- ").Append(change.OldPath is null ? change.Path : $"{change.OldPath} → {change.Path}");
                if (stats.TryGetValue(change.Path, out var stat))
                {
                    message.Append(stat.Added is null || stat.Deleted is null
                        ? $" ({FlowMessages.Get("Commit_Binary")})"
                        : $" (+{stat.Added} -{stat.Deleted})");
                }

                message.AppendLine();
            }
        }

        if (changes.Count > MaxListedFiles)
        {
            message.AppendLine(FlowMessages.Format("Commit_More", changes.Count - MaxListedFiles));
        }

        var tickets = FindTickets(branchName);
        if (tickets.Count > 0)
        {
            message.AppendLine().AppendLine(FlowMessages.Format("Commit_Refs", string.Join(", ", tickets)));
        }

        return message.ToString().TrimEnd();
    }

    /// <summary>Ticket references in a branch name, e.g. "feature/ABC-123-login" → ABC-123, "feature/42-fix" is not one (#42 is).</summary>
    public static IReadOnlyList<string> FindTickets(string? branchName) =>
        string.IsNullOrEmpty(branchName)
            ? []
            : TicketPattern().Matches(branchName).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToArray();

    private static string Subject(IReadOnlyList<FileChange> changes)
    {
        string subject;
        if (changes.Count == 1)
        {
            var change = changes[0];
            subject = change.Kind == ChangeKind.Renamed && change.OldPath is not null
                ? FlowMessages.Format("Commit_Subject_One_Renamed", FileName(change.OldPath), FileName(change.Path))
                : FlowMessages.Format("Commit_Subject_One_" + change.Kind, FileName(change.Path));
        }
        else
        {
            var kinds = changes.Select(c => c.Kind).Distinct().ToArray();
            subject = FlowMessages.Format(kinds.Length == 1 ? "Commit_Subject_Many_" + kinds[0] : "Commit_Subject_Many_Mixed", changes.Count);
            var directory = CommonDirectory(changes.Select(c => c.Path));
            if (directory.Length > 0)
            {
                subject += FlowMessages.Format("Commit_Subject_In", directory);
            }
        }

        return subject.Length <= MaxSubjectLength ? subject : subject[..(MaxSubjectLength - 1)] + "…";
    }

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static string CommonDirectory(IEnumerable<string> paths)
    {
        string[]? common = null;
        foreach (var path in paths)
        {
            var directory = path.Split('/')[..^1];
            if (common is null)
            {
                common = directory;
                continue;
            }

            var length = 0;
            while (length < common.Length && length < directory.Length && common[length] == directory[length])
            {
                length++;
            }

            common = common[..length];
        }

        return common is null ? string.Empty : string.Join('/', common);
    }

    [GeneratedRegex(@"(?<![A-Za-z0-9])([A-Z][A-Z0-9]+-\d+|#\d+)(?![0-9])")]
    private static partial Regex TicketPattern();
}
