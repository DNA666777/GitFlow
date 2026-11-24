using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace GitFlow.Core.PullRequests;

/// <summary>GitHub REST API (https://docs.github.com/rest/pulls/pulls).</summary>
public sealed class GitHubPullRequestProvider(HttpClient http) : IPullRequestProvider
{
    public HostingPlatform Platform => HostingPlatform.GitHub;

    public async Task<PullRequestInfo> CreateAsync(RemoteRepository repository, HostCredential credential, PullRequestDraft draft, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Post, repository, credential, "pulls");
        request.Content = JsonContent.Create(new { title = draft.Title, head = draft.Source, @base = draft.Target, body = draft.Description });
        using var json = await SendAsync(request, ct).ConfigureAwait(false);
        return Read(json.RootElement);
    }

    public async Task<PullRequestInfo?> FindAsync(RemoteRepository repository, HostCredential credential, string source, string target, CancellationToken ct)
    {
        var query = $"pulls?state=all&sort=created&direction=desc&per_page=1&head={Uri.EscapeDataString(repository.Owner + ":" + source)}&base={Uri.EscapeDataString(target)}";
        using var request = Request(HttpMethod.Get, repository, credential, query);
        using var json = await SendAsync(request, ct).ConfigureAwait(false);
        return json.RootElement.GetArrayLength() == 0 ? null : Read(json.RootElement[0]);
    }

    public async Task<PullRequestInfo?> GetAsync(RemoteRepository repository, HostCredential credential, int id, CancellationToken ct)
    {
        using var request = Request(HttpMethod.Get, repository, credential, $"pulls/{id}");
        try
        {
            using var json = await SendAsync(request, ct).ConfigureAwait(false);
            return Read(json.RootElement);
        }
        catch (PullRequestApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, RemoteRepository repository, HostCredential credential, string path)
    {
        var uri = new Uri(repository.ApiBase, $"repos/{Uri.EscapeDataString(repository.Owner)}/{Uri.EscapeDataString(repository.Name)}/{path}");
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("GitFlowVS", "1.0"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential.Secret);
        return request;
    }

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new PullRequestApiException((int)response.StatusCode, ErrorMessage(body) ?? response.ReasonPhrase ?? response.StatusCode.ToString());
        }

        return JsonDocument.Parse(body);
    }

    private static string? ErrorMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var message = json.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            if (json.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                var details = errors.EnumerateArray()
                    .Select(e => e.TryGetProperty("message", out var em) ? em.GetString() : null)
                    .OfType<string>();
                message = string.Join(" ", new[] { message }.Concat(details).OfType<string>());
            }

            return message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static PullRequestInfo Read(JsonElement pr)
    {
        var merged = pr.TryGetProperty("merged_at", out var mergedAt) && mergedAt.ValueKind == JsonValueKind.String;
        var state = merged ? PullRequestState.Merged
            : pr.GetProperty("state").GetString() == "open" ? PullRequestState.Open
            : PullRequestState.Closed;
        return new PullRequestInfo(
            pr.GetProperty("number").GetInt32(),
            new Uri(pr.GetProperty("html_url").GetString()!),
            state,
            pr.GetProperty("head").GetProperty("ref").GetString()!,
            pr.GetProperty("base").GetProperty("ref").GetString()!,
            pr.GetProperty("title").GetString() ?? string.Empty,
            merged && pr.TryGetProperty("merge_commit_sha", out var sha) ? sha.GetString() : null);
    }
}
