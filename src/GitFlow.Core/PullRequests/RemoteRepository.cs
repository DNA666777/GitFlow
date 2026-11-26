using System.Text.RegularExpressions;

namespace GitFlow.Core.PullRequests;

public enum HostingPlatform
{
    GitHub,
    AzureDevOps,
}

/// <summary>
/// The hosted repository behind the "origin" remote.
/// GitHub: <see cref="Owner"/>/<see cref="Name"/>. Azure DevOps: organisation (or server) <see cref="Owner"/>, <see cref="Project"/>, repository <see cref="Name"/>.
/// </summary>
/// <param name="ServerUrl">
/// Azure DevOps outside dev.azure.com ({org}.visualstudio.com or an Azure DevOps Server): the URL before <c>_git/</c>,
/// ending with '/'. Null for GitHub and dev.azure.com.
/// </param>
/// <param name="OnPremises">Azure DevOps Server (declared by the user): older REST API, Windows authentication possible.</param>
public sealed record RemoteRepository(
    HostingPlatform Platform, string Owner, string? Project, string Name, Uri? ServerUrl = null, bool OnPremises = false)
{
    /// <summary>Protocol used to ask git for credentials (https, like git's own push/fetch; http for an http server).</summary>
    public string CredentialProtocol => ServerUrl?.Scheme ?? "https";

    /// <summary>Host (with its port when it is not the default one) used to ask git for credentials.</summary>
    public string CredentialHost => Platform == HostingPlatform.GitHub
        ? "github.com"
        : ServerUrl?.Authority.ToLowerInvariant() ?? "dev.azure.com";

    /// <summary>Path sent with the credential request (dev.azure.com credentials are per organisation).</summary>
    public string? CredentialPath => Platform == HostingPlatform.AzureDevOps && ServerUrl is null ? Owner : null;

    /// <summary>Base of the REST API.</summary>
    public Uri ApiBase => Platform == HostingPlatform.GitHub
        ? new Uri("https://api.github.com/")
        : ServerUrl ?? new Uri($"https://dev.azure.com/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Project!)}/");

    /// <summary>Web page of the repository.</summary>
    public Uri WebUrl => Platform == HostingPlatform.GitHub
        ? new Uri($"https://github.com/{Owner}/{Name}")
        : new Uri(ApiBase, $"_git/{Uri.EscapeDataString(Name)}");

    /// <summary>
    /// Recognises a GitHub or Azure DevOps remote URL (https or ssh, with or without user name or port); null for anything else.
    /// Azure DevOps: dev.azure.com, ssh.dev.azure.com, {org}.visualstudio.com and vs-ssh.visualstudio.com, including the
    /// project-less form (org/_git/repo: the project has the repository's name), DefaultCollection, _optimized/_full and _ssh.
    /// </summary>
    /// <param name="azureDevOpsServer">
    /// The user declared that origin is an Azure DevOps server: any URL of the form …/_git/repo is then accepted
    /// (company servers have URLs that cannot be recognised, e.g. https://tfs.company.com/tfs/Collection/Project/_git/Repo).
    /// </param>
    public static RemoteRepository? TryParse(string? url, bool azureDevOpsServer = false)
    {
        if (string.IsNullOrWhiteSpace(url) || !TrySplit(url.Trim(), out var parts))
        {
            return null;
        }

        return Recognise(parts) ?? (azureDevOpsServer ? FromServer(parts) : null);
    }

    private static RemoteRepository? Recognise(UrlParts parts)
    {
        var (_, user, host, _, segments) = parts;
        if (host is "github.com" or "www.github.com" or "ssh.github.com")
        {
            return segments.Length == 2 ? new(HostingPlatform.GitHub, segments[0], null, Clean(segments[1])) : null;
        }

        if (host == "dev.azure.com")
        {
            // org/project/_git/repo
            return segments.Length > 0 ? FromPath(segments[1..], "_git", segments[0], legacyHost: false) : null;
        }

        if (host is "ssh.dev.azure.com" or "vs-ssh.visualstudio.com")
        {
            var legacy = host == "vs-ssh.visualstudio.com";
            if (segments.Length == 4 && segments[0].Equals("v3", StringComparison.OrdinalIgnoreCase))
            {
                // v3/org/project/repo
                return Azure(segments[1], segments[2], segments[3], legacy);
            }

            // Old ssh form on vs-ssh: the organisation is the user name, then (DefaultCollection/)project/_ssh/repo.
            return legacy && user is not null ? FromPath(segments, "_ssh", user, legacyHost: true) : null;
        }

        const string legacySuffix = ".visualstudio.com";
        if (host.EndsWith(legacySuffix, StringComparison.Ordinal) && host.IndexOf('.') == host.Length - legacySuffix.Length)
        {
            return FromPath(segments, "_git", host[..^legacySuffix.Length], legacyHost: true);
        }

        return null;
    }

    /// <summary>Azure DevOps path after the organisation: (DefaultCollection/)(project/){marker}/(_optimized/|_full/)repo.</summary>
    private static RemoteRepository? FromPath(string[] segments, string marker, string org, bool legacyHost)
    {
        if (!TrySplitAtMarker(segments, marker, out var before, out var repo))
        {
            return null;
        }

        if (before.Length > 0 && before[0].Equals("DefaultCollection", StringComparison.OrdinalIgnoreCase))
        {
            before = before[1..];
        }

        // No project segment: Azure DevOps names the default repository after its project.
        return before.Length > 1 ? null : Azure(org, before.Length == 1 ? before[0] : repo, repo, legacyHost);
    }

    private static RemoteRepository Azure(string org, string project, string repo, bool legacyHost)
    {
        var organisation = Uri.UnescapeDataString(org);
        var projectName = Clean(project);
        Uri? server = legacyHost
            ? new Uri($"https://{organisation.ToLowerInvariant()}.visualstudio.com/{Uri.EscapeDataString(projectName)}/")
            : null;
        return new(HostingPlatform.AzureDevOps, organisation, projectName, Clean(repo), server);
    }

    /// <summary>
    /// Azure DevOps Server: [scheme://]host[:port]/(any path)/_git/(_optimized/|_full/)repo. The REST API lives under the
    /// path before _git (collection and project); over ssh, the API is reached over https on the same host.
    /// </summary>
    private static RemoteRepository? FromServer(UrlParts parts)
    {
        if (!TrySplitAtMarker(parts.Segments, "_git", out var before, out var repo) || before.Length == 0)
        {
            return null;
        }

        var web = parts.Scheme is "http" or "https";
        var authority = web && parts.Port is { } port ? $"{parts.Host}:{port}" : parts.Host;
        var server = new Uri($"{(parts.Scheme == "http" ? "http" : "https")}://{authority}/{string.Join('/', before)}/");
        return new(HostingPlatform.AzureDevOps, parts.Host, Uri.UnescapeDataString(before[^1]), Clean(repo), server, OnPremises: true);
    }

    private static bool TrySplitAtMarker(string[] segments, string marker, out string[] before, out string repo)
    {
        before = [];
        repo = "";
        var index = Array.FindIndex(segments, s => s.Equals(marker, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return false;
        }

        var after = segments[(index + 1)..];
        if (after.Length == 2 && (after[0].Equals("_optimized", StringComparison.OrdinalIgnoreCase) || after[0].Equals("_full", StringComparison.OrdinalIgnoreCase)))
        {
            after = after[1..];
        }

        if (after.Length != 1)
        {
            return false;
        }

        before = segments[..index];
        repo = after[0];
        return true;
    }

    /// <param name="Scheme">Lower case; "ssh" for git's scp-like syntax.</param>
    /// <param name="Segments">Path segments, still URL-encoded.</param>
    private sealed record UrlParts(string Scheme, string? User, string Host, int? Port, string[] Segments);

    /// <summary>
    /// Parts of scheme://[user@]host[:port]/path or of git's scp-like syntax [user@]host:path.
    /// Local paths (C:\…, ../x, file://…) give false.
    /// </summary>
    private static bool TrySplit(string url, out UrlParts parts)
    {
        parts = new UrlParts("", null, "", null, []);
        string scheme;
        var match = Regex.Match(url, @"^(?<scheme>[a-z][a-z0-9+.-]*)://(?<authority>[^/]*)(?<path>/.*)?$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            scheme = match.Groups["scheme"].Value.ToLowerInvariant();
            if (scheme == "file")
            {
                return false;
            }
        }
        else
        {
            // scp-like: [user@]host:path (a single letter before ':' is a Windows drive).
            match = Regex.Match(url, @"^(?<authority>[^/\\:]{2,}):(?<path>[^\\]*)$");
            if (!match.Success)
            {
                return false;
            }

            scheme = "ssh";
        }

        var authority = match.Groups["authority"].Value;
        var at = authority.LastIndexOf('@');
        var user = at > 0 ? Uri.UnescapeDataString(authority[..at]) : null;
        var host = (at >= 0 ? authority[(at + 1)..] : authority).ToLowerInvariant();
        int? port = null;
        var colon = host.IndexOf(':');
        if (colon >= 0)
        {
            port = int.TryParse(host[(colon + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : null;
            host = host[..colon];
        }

        if (host.Length == 0)
        {
            return false;
        }

        parts = new UrlParts(scheme, user, host, port, match.Groups["path"].Value.Split('/', StringSplitOptions.RemoveEmptyEntries));
        return true;
    }

    private static string Clean(string value) => Uri.UnescapeDataString(value.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? value[..^4] : value);
}
