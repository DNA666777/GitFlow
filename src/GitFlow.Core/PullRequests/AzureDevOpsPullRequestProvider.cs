using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GitFlow.Core.PullRequests;

/// <summary>
/// Azure DevOps (Azure Repos) REST API (https://learn.microsoft.com/rest/api/azure/devops/git/pull-requests):
/// 7.1 on the cloud, 4.1 on an Azure DevOps Server (supported from TFS 2018 on).
/// The secret from the Git Credential Manager is either a PAT (Basic) or an Entra ID access token (Bearer);
/// on a server, without a secret, the Windows session's credentials are used (<paramref name="windowsHttp"/>).
/// </summary>
/// <param name="windowsHttp">Client that sends the Windows credentials (NTLM / Kerberos); only used for an Azure DevOps Server.</param>
public sealed class AzureDevOpsPullRequestProvider(HttpClient http, HttpClient? windowsHttp = null) : IPullRequestProvider
{

    public HostingPlatform Platform => HostingPlatform.AzureDevOps;

    public async Task<PullRequestInfo> CreateAsync(RemoteRepository repository, HostCredential credential, PullRequestDraft draft, CancellationToken ct)
    {
        using var json = await SendAsync(
            HttpMethod.Post,
            repository,
            credential,
            $"pullrequests?{ApiVersion(repository)}",
            new
            {
                sourceRefName = "refs/heads/" + draft.Source,
                targetRefName = "refs/heads/" + draft.Target,
                title = draft.Title,
                description = draft.Description,
            },
            ct).ConfigureAwait(false);
        return Read(repository, json.RootElement);
    }

    public async Task<PullRequestInfo?> FindAsync(RemoteRepository repository, HostCredential credential, string source, string target, CancellationToken ct)
    {
        var query = "pullrequests?searchCriteria.status=all&$top=1"
            + "&searchCriteria.sourceRefName=" + Uri.EscapeDataString("refs/heads/" + source)
            + "&searchCriteria.targetRefName=" + Uri.EscapeDataString("refs/heads/" + target)
            + "&" + ApiVersion(repository);
        using var json = await SendAsync(HttpMethod.Get, repository, credential, query, null, ct).ConfigureAwait(false);
        var values = json.RootElement.GetProperty("value");
        return values.GetArrayLength() == 0 ? null : Read(repository, values[0]);
    }

    public async Task<PullRequestInfo?> GetAsync(RemoteRepository repository, HostCredential credential, int id, CancellationToken ct)
    {
        try
        {
            using var json = await SendAsync(HttpMethod.Get, repository, credential, $"pullrequests/{id}?{ApiVersion(repository)}", null, ct).ConfigureAwait(false);
            return Read(repository, json.RootElement);
        }
        catch (PullRequestApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private static string ApiVersion(RemoteRepository repository) => repository.OnPremises ? "api-version=4.1" : "api-version=7.1";

    private async Task<JsonDocument> SendAsync(HttpMethod method, RemoteRepository repository, HostCredential credential, string path, object? body, CancellationToken ct)
    {
        if (credential.IsWindowsIntegrated)
        {
            return await SendOnceAsync(windowsHttp ?? http, method, repository, credential, path, body, bearer: false, ct).ConfigureAwait(false);
        }

        // An Entra ID access token is a JWT (Bearer); otherwise a PAT (Basic). On an authentication failure, try the other scheme once.
        var bearerFirst = credential.Secret.StartsWith("eyJ", StringComparison.Ordinal);
        try
        {
            return await SendOnceAsync(http, method, repository, credential, path, body, bearerFirst, ct).ConfigureAwait(false);
        }
        catch (PullRequestApiException ex) when (ex.IsAuthenticationError)
        {
            return await SendOnceAsync(http, method, repository, credential, path, body, !bearerFirst, ct).ConfigureAwait(false);
        }
    }

    private static async Task<JsonDocument> SendOnceAsync(
        HttpClient client, HttpMethod method, RemoteRepository repository, HostCredential credential, string path, object? body, bool bearer, CancellationToken ct)
    {
        var uri = new Uri(repository.ApiBase, $"_apis/git/repositories/{Uri.EscapeDataString(repository.Name)}/{path}");
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!credential.IsWindowsIntegrated)
        {
            request.Headers.Authorization = bearer
                ? new AuthenticationHeaderValue("Bearer", credential.Secret)
                : new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + credential.Secret)));
        }
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Azure DevOps answers an unauthenticated call with 203 + an HTML sign-in page.
        var isJson = response.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
        if (!response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NonAuthoritativeInformation || !isJson)
        {
            var status = response.IsSuccessStatusCode ? 203 : (int)response.StatusCode;
            throw new PullRequestApiException(status, ErrorMessage(text) ?? response.ReasonPhrase ?? status.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return JsonDocument.Parse(text);
    }

    private static string? ErrorMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            return json.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PullRequestInfo Read(RemoteRepository repository, JsonElement pr)
    {
        var id = pr.GetProperty("pullRequestId").GetInt32();
        var state = pr.GetProperty("status").GetString() switch
        {
            "completed" => PullRequestState.Merged,
            "abandoned" => PullRequestState.Closed,
            _ => PullRequestState.Open,
        };
        string? mergeCommit = null;
        if (state == PullRequestState.Merged && pr.TryGetProperty("lastMergeCommit", out var commit) && commit.TryGetProperty("commitId", out var commitId))
        {
            mergeCommit = commitId.GetString();
        }

        return new PullRequestInfo(
            id,
            new Uri($"{repository.WebUrl}/pullrequest/{id}"),
            state,
            StripHeads(pr.GetProperty("sourceRefName").GetString()),
            StripHeads(pr.GetProperty("targetRefName").GetString()),
            pr.GetProperty("title").GetString() ?? string.Empty,
            mergeCommit);
    }

    private static string StripHeads(string? reference) =>
        reference is not null && reference.StartsWith("refs/heads/", StringComparison.Ordinal) ? reference["refs/heads/".Length..] : reference ?? string.Empty;
}
