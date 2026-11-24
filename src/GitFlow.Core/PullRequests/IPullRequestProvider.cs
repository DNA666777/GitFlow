namespace GitFlow.Core.PullRequests;

public enum PullRequestState
{
    Open,
    Merged,
    Closed,
}

/// <summary>A pull request as seen on the hosting platform.</summary>
/// <param name="MergeCommit">Commit created on the target by the merge (null while not merged).</param>
public sealed record PullRequestInfo(
    int Id,
    Uri WebUrl,
    PullRequestState State,
    string Source,
    string Target,
    string Title,
    string? MergeCommit);

/// <summary>
/// One hosting platform (GitHub, Azure DevOps). The extension creates and follows pull requests;
/// it never approves nor merges them: the review stays human.
/// </summary>
public interface IPullRequestProvider
{
    HostingPlatform Platform { get; }

    Task<PullRequestInfo> CreateAsync(RemoteRepository repository, HostCredential credential, PullRequestDraft draft, CancellationToken ct);

    /// <summary>Most recent pull request from <paramref name="source"/> to <paramref name="target"/>, any state; null when none.</summary>
    Task<PullRequestInfo?> FindAsync(RemoteRepository repository, HostCredential credential, string source, string target, CancellationToken ct);

    Task<PullRequestInfo?> GetAsync(RemoteRepository repository, HostCredential credential, int id, CancellationToken ct);
}

/// <summary>The platform API answered with an error (authentication, permissions, validation...).</summary>
public sealed class PullRequestApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public bool IsAuthenticationError => StatusCode is 401 or 403 or 203;
}
