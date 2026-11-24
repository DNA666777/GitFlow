using GitFlow.Core.Git;

namespace GitFlow.Core.PullRequests;

/// <summary>A credential returned by git. <see cref="ToString"/> never shows the secret.</summary>
public sealed class HostCredential(string username, string secret)
{
    public string Username { get; } = username;

    public string Secret { get; } = secret;

    public override string ToString() => $"{Username}:***";
}

/// <summary>
/// Gets the credentials git already uses for push/fetch over https, through <c>git credential fill</c>
/// (normally the Git Credential Manager). Nothing is stored by the extension, nothing is written to logs,
/// and stored credentials are never rejected/erased (they also serve the user's pushes).
/// </summary>
public sealed class GitCredentialProvider(IGitRunner runner)
{
    public async Task<HostCredential?> GetAsync(string repositoryPath, RemoteRepository remote, CancellationToken ct = default)
    {
        var request = $"protocol=https\nhost={remote.CredentialHost}\n";
        if (remote.CredentialPath is { } path)
        {
            request += $"path={path}\n";
        }

        request += "\n";
        var result = await runner.RunAsync(repositoryPath, ["credential", "fill"], request, ct).ConfigureAwait(false);
        return result.Success ? Parse(result.StandardOutput) : null;
    }

    internal static HostCredential? Parse(string output)
    {
        string? username = null, password = null;
        foreach (var line in output.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator];
            var value = line[(separator + 1)..].TrimEnd('\r');
            if (key == "username")
            {
                username = value;
            }
            else if (key == "password")
            {
                password = value;
            }
        }

        return string.IsNullOrEmpty(password) ? null : new HostCredential(username ?? string.Empty, password);
    }
}
