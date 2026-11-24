namespace GitFlow.Core.Model;

public enum ChangeKind
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Conflicted,
}

/// <summary>One changed file of the work tree.</summary>
/// <param name="Path">Path relative to the repository root, with '/' separators.</param>
/// <param name="OldPath">Previous path for a rename, otherwise null.</param>
/// <param name="IsStaged">The change (or part of it) is in the index.</param>
/// <param name="IsUnstaged">The work tree has changes not in the index (always true for an untracked file).</param>
public sealed record FileChange(ChangeKind Kind, string Path, string? OldPath, bool IsStaged, bool IsUnstaged);

/// <summary>Lines added / deleted in a file (null for binary files).</summary>
public sealed record FileStat(int? Added, int? Deleted);

public enum CommitMode
{
    /// <summary>Something is staged: only the index is committed (the user's choice is respected).</summary>
    StagedOnly,

    /// <summary>Nothing is staged: every change, untracked files included, is committed (git add -A).</summary>
    AllChanges,
}

/// <summary>What a commit would contain right now, with a generated message.</summary>
public sealed record CommitPreview
{
    public CommitMode Mode { get; init; }

    /// <summary>The changes that will be committed in <see cref="Mode"/>.</summary>
    public IReadOnlyList<FileChange> Changes { get; init; } = [];

    public string SuggestedMessage { get; init; } = string.Empty;

    /// <summary>Why committing is not possible now (None when it is).</summary>
    public FlowError BlockedBy { get; init; }

    public string? BlockedDetail { get; init; }

    public bool CanCommit => BlockedBy == FlowError.None && Changes.Count > 0;
}
