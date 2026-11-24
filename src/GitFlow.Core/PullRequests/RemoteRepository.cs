using System.Text.RegularExpressions;

namespace GitFlow.Core.PullRequests;

public enum HostingPlatform
{
    GitHub,
    AzureDevOps,
}

/// <summary>
/// The hosted repository behind the "origin" remote.
/// GitHub: <see cref="Owner"/>/<see cref="Name"/>. Azure DevOps: organisation <see cref="Owner"/>, <see cref="Project"/>, repository <see cref="Name"/>.
/// </summary>
public sealed record RemoteRepository(HostingPlatform Platform, string Owner, string? Project, string Name, string? LegacyHost = null)
{
    /// <summary>Host used to ask git for credentials (always https, like git's own push/fetch over https).</summary>
    public string CredentialHost => Platform == HostingPlatform.GitHub ? "github.com" : LegacyHost ?? "dev.azure.com";

    /// <summary>Path sent with the credential request (Azure DevOps credentials are per organisation).</summary>
    public string? CredentialPath => Platform == HostingPlatform.AzureDevOps && LegacyHost is null ? Owner : null;

    /// <summary>Base of the REST API.</summary>
    public Uri ApiBase => Platform == HostingPlatform.GitHub
        ? new Uri("https://api.github.com/")
        : new Uri(LegacyHost is null
            ? $"https://dev.azure.com/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Project!)}/"
            : $"https://{LegacyHost}/{Uri.EscapeDataString(Project!)}/");

    /// <summary>Web page of the repository.</summary>
    public Uri WebUrl => Platform == HostingPlatform.GitHub
        ? new Uri($"https://github.com/{Owner}/{Name}")
        : new Uri(ApiBase, $"_git/{Uri.EscapeDataString(Name)}");

    /// <summary>Recognises a GitHub or Azure DevOps remote URL (https or ssh); null for anything else.</summary>
    public static RemoteRepository? TryParse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        url = url.Trim();
        foreach (var (pattern, build) in Patterns)
        {
            var match = pattern.Match(url);
            if (match.Success)
            {
                return build(match);
            }
        }

        return null;
    }

    private static string Clean(string value) => Uri.UnescapeDataString(value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value);

    private static readonly (Regex Pattern, Func<Match, RemoteRepository> Build)[] Patterns =
    [
        // https://github.com/owner/repo(.git), https://user@github.com/owner/repo
        (new Regex(@"^https?://([^@/]+@)?(www\.)?github\.com/(?<owner>[^/]+)/(?<repo>[^/]+?)/?$", RegexOptions.IgnoreCase),
            m => new(HostingPlatform.GitHub, m.Groups["owner"].Value, null, Clean(m.Groups["repo"].Value))),
        // git@github.com:owner/repo.git, ssh://git@github.com/owner/repo.git
        (new Regex(@"^(ssh://)?git@github\.com[:/](?<owner>[^/]+)/(?<repo>[^/]+?)/?$", RegexOptions.IgnoreCase),
            m => new(HostingPlatform.GitHub, m.Groups["owner"].Value, null, Clean(m.Groups["repo"].Value))),
        // https://(org@)dev.azure.com/org/project/_git/repo
        (new Regex(@"^https://([^@/]+@)?dev\.azure\.com/(?<org>[^/]+)/(?<project>[^/]+)/_git/(?<repo>[^/]+?)/?$", RegexOptions.IgnoreCase),
            m => new(HostingPlatform.AzureDevOps, Clean(m.Groups["org"].Value), Clean(m.Groups["project"].Value), Clean(m.Groups["repo"].Value))),
        // git@ssh.dev.azure.com:v3/org/project/repo
        (new Regex(@"^(ssh://)?git@ssh\.dev\.azure\.com[:/]v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)/?$", RegexOptions.IgnoreCase),
            m => new(HostingPlatform.AzureDevOps, Clean(m.Groups["org"].Value), Clean(m.Groups["project"].Value), Clean(m.Groups["repo"].Value))),
        // https://org.visualstudio.com/(DefaultCollection/)project/_git/repo
        (new Regex(@"^https://([^@/]+@)?(?<org>[^./]+)\.visualstudio\.com/(DefaultCollection/)?(?<project>[^/]+)/_git/(?<repo>[^/]+?)/?$", RegexOptions.IgnoreCase),
            m => new(HostingPlatform.AzureDevOps, m.Groups["org"].Value, Clean(m.Groups["project"].Value), Clean(m.Groups["repo"].Value), $"{m.Groups["org"].Value}.visualstudio.com")),
        // org@vs-ssh.visualstudio.com:v3/org/project/repo
        (new Regex(@"^(ssh://)?[^@]+@vs-ssh\.visualstudio\.com[:/]v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)/?$", RegexOptions.IgnoreCase),
            m => new(HostingPlatform.AzureDevOps, m.Groups["org"].Value, Clean(m.Groups["project"].Value), Clean(m.Groups["repo"].Value), $"{m.Groups["org"].Value}.visualstudio.com")),
    ];
}
