using System.Net;
using System.Text.Json;
using GitFlow.Core.PullRequests;
using Shouldly;

namespace GitFlow.Core.Tests;

public class PullRequestProviderTests
{
    private static readonly HostCredential Pat = new("me", "pat-secret");
    private static readonly PullRequestDraft Draft = new("feature/login", "develop", "Feature: login", "Description « é »");

    // ---------- GitHub ----------

    private static readonly RemoteRepository GitHub = RemoteRepository.TryParse("https://github.com/owner/repo.git")!;

    private const string GitHubOpenPr = """
        {"number":12,"html_url":"https://github.com/owner/repo/pull/12","state":"open","merged_at":null,
         "merge_commit_sha":"tmp","title":"Feature: login","head":{"ref":"feature/login"},"base":{"ref":"develop"}}
        """;

    [Fact]
    public async Task GitHub_create_posts_the_draft()
    {
        var http = new FakeHttpHandler().Respond(HttpStatusCode.Created, GitHubOpenPr);

        var pr = await new GitHubPullRequestProvider(new HttpClient(http)).CreateAsync(GitHub, Pat, Draft, default);

        pr.ShouldBe(new PullRequestInfo(12, new Uri("https://github.com/owner/repo/pull/12"), PullRequestState.Open, "feature/login", "develop", "Feature: login", null));
        var (request, body) = http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri.ShouldBe(new Uri("https://api.github.com/repos/owner/repo/pulls"));
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("pat-secret");
        request.Headers.UserAgent.ToString().ShouldBe("GitFlowVS/1.0");
        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("head").GetString().ShouldBe("feature/login");
        json.RootElement.GetProperty("base").GetString().ShouldBe("develop");
        json.RootElement.GetProperty("title").GetString().ShouldBe("Feature: login");
        json.RootElement.GetProperty("body").GetString().ShouldBe("Description « é »");
    }

    [Fact]
    public async Task GitHub_find_filters_on_head_and_base()
    {
        var http = new FakeHttpHandler().Respond(HttpStatusCode.OK, "[" + GitHubOpenPr + "]");

        var pr = await new GitHubPullRequestProvider(new HttpClient(http)).FindAsync(GitHub, Pat, "feature/login", "develop", default);

        pr!.Id.ShouldBe(12);
        http.Requests[0].Request.RequestUri!.Query.ShouldBe("?state=all&sort=created&direction=desc&per_page=1&head=owner%3Afeature%2Flogin&base=develop");
    }

    [Fact]
    public async Task GitHub_find_returns_null_when_none()
    {
        var http = new FakeHttpHandler().Respond(HttpStatusCode.OK, "[]");

        (await new GitHubPullRequestProvider(new HttpClient(http)).FindAsync(GitHub, Pat, "feature/x", "develop", default)).ShouldBeNull();
    }

    [Fact]
    public async Task GitHub_merged_pr_has_its_merge_commit()
    {
        var http = new FakeHttpHandler().Respond(HttpStatusCode.OK, """
            {"number":3,"html_url":"https://github.com/owner/repo/pull/3","state":"closed","merged_at":"2026-11-24T10:00:00Z",
             "merge_commit_sha":"abc123","title":"Release 1.1.0","head":{"ref":"release/1.1.0"},"base":{"ref":"master"}}
            """);

        var pr = await new GitHubPullRequestProvider(new HttpClient(http)).GetAsync(GitHub, Pat, 3, default);

        pr!.State.ShouldBe(PullRequestState.Merged);
        pr.MergeCommit.ShouldBe("abc123");
    }

    [Fact]
    public async Task GitHub_closed_without_merge_and_missing_pr()
    {
        var http = new FakeHttpHandler()
            .Respond(HttpStatusCode.OK, GitHubOpenPr.Replace("\"open\"", "\"closed\""))
            .Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
        var provider = new GitHubPullRequestProvider(new HttpClient(http));

        (await provider.GetAsync(GitHub, Pat, 12, default))!.State.ShouldBe(PullRequestState.Closed);
        (await provider.GetAsync(GitHub, Pat, 99, default)).ShouldBeNull();
    }

    [Fact]
    public async Task GitHub_errors_carry_the_api_message()
    {
        var http = new FakeHttpHandler().Respond(HttpStatusCode.UnprocessableEntity,
            """{"message":"Validation Failed","errors":[{"message":"A pull request already exists for owner:feature/login."}]}""");

        var ex = await Should.ThrowAsync<PullRequestApiException>(() =>
            new GitHubPullRequestProvider(new HttpClient(http)).CreateAsync(GitHub, Pat, Draft, default));

        ex.StatusCode.ShouldBe(422);
        ex.Message.ShouldBe("Validation Failed A pull request already exists for owner:feature/login.");
        ex.Message.ShouldNotContain("pat-secret");
    }

    // ---------- Azure DevOps ----------

    private static readonly RemoteRepository Azure = RemoteRepository.TryParse("https://dev.azure.com/contoso/My%20Project/_git/Repo")!;

    private const string AzureActivePr = """
        {"pullRequestId":7,"status":"active","title":"Feature: login",
         "sourceRefName":"refs/heads/feature/login","targetRefName":"refs/heads/develop"}
        """;

    [Fact]
    public async Task Azure_create_posts_refs_with_basic_auth_for_a_pat()
    {
        var http = new FakeHttpHandler().Respond(HttpStatusCode.Created, AzureActivePr);

        var pr = await new AzureDevOpsPullRequestProvider(new HttpClient(http)).CreateAsync(Azure, Pat, Draft, default);

        pr.ShouldBe(new PullRequestInfo(7, new Uri("https://dev.azure.com/contoso/My%20Project/_git/Repo/pullrequest/7"), PullRequestState.Open, "feature/login", "develop", "Feature: login", null));
        var (request, body) = http.Requests.ShouldHaveSingleItem();
        request.RequestUri.ShouldBe(new Uri("https://dev.azure.com/contoso/My%20Project/_apis/git/repositories/Repo/pullrequests?api-version=7.1"));
        request.Headers.Authorization!.Scheme.ShouldBe("Basic");
        request.Headers.Authorization.Parameter.ShouldBe(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(":pat-secret")));
        using var json = JsonDocument.Parse(body!);
        json.RootElement.GetProperty("sourceRefName").GetString().ShouldBe("refs/heads/feature/login");
        json.RootElement.GetProperty("targetRefName").GetString().ShouldBe("refs/heads/develop");
        json.RootElement.GetProperty("description").GetString().ShouldBe("Description « é »");
    }

    [Fact]
    public async Task Azure_uses_bearer_for_an_entra_token()
    {
        var http = new FakeHttpHandler().Respond(HttpStatusCode.OK, """{"value":[]}""");

        await new AzureDevOpsPullRequestProvider(new HttpClient(http)).FindAsync(Azure, new HostCredential("u", "eyJhbGciOi.token"), "feature/x", "develop", default);

        http.Requests[0].Request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
    }

    [Fact]
    public async Task Azure_retries_with_the_other_scheme_after_a_sign_in_page()
    {
        var http = new FakeHttpHandler()
            .Respond(HttpStatusCode.NonAuthoritativeInformation, "<html>Sign in</html>", "text/html")
            .Respond(HttpStatusCode.OK, """{"value":[]}""");

        var pr = await new AzureDevOpsPullRequestProvider(new HttpClient(http)).FindAsync(Azure, Pat, "feature/x", "develop", default);

        pr.ShouldBeNull();
        http.Requests.Select(r => r.Request.Headers.Authorization!.Scheme).ShouldBe(["Basic", "Bearer"]);
    }

    [Fact]
    public async Task Azure_find_query_and_completed_pr()
    {
        var http = new FakeHttpHandler().Respond(HttpStatusCode.OK, """
            {"value":[{"pullRequestId":9,"status":"completed","title":"Release 1.1.0",
              "sourceRefName":"refs/heads/release/1.1.0","targetRefName":"refs/heads/master",
              "lastMergeCommit":{"commitId":"def456"}}]}
            """);

        var pr = await new AzureDevOpsPullRequestProvider(new HttpClient(http)).FindAsync(Azure, Pat, "release/1.1.0", "master", default);

        pr!.State.ShouldBe(PullRequestState.Merged);
        pr.MergeCommit.ShouldBe("def456");
        pr.Source.ShouldBe("release/1.1.0");
        http.Requests[0].Request.RequestUri!.Query.ShouldBe(
            "?searchCriteria.status=all&$top=1&searchCriteria.sourceRefName=refs%2Fheads%2Frelease%2F1.1.0&searchCriteria.targetRefName=refs%2Fheads%2Fmaster&api-version=7.1");
    }

    [Fact]
    public async Task Azure_abandoned_and_errors()
    {
        var http = new FakeHttpHandler()
            .Respond(HttpStatusCode.OK, AzureActivePr.Replace("active", "abandoned"))
            .Respond(HttpStatusCode.Conflict, """{"message":"TF401179: An active pull request for the source and target branch already exists."}""");
        var provider = new AzureDevOpsPullRequestProvider(new HttpClient(http));

        (await provider.GetAsync(Azure, Pat, 7, default))!.State.ShouldBe(PullRequestState.Closed);
        var ex = await Should.ThrowAsync<PullRequestApiException>(() => provider.CreateAsync(Azure, Pat, Draft, default));
        ex.StatusCode.ShouldBe(409);
        ex.Message.ShouldStartWith("TF401179");
    }

    [Fact]
    public void Legacy_visualstudio_com_api_base()
    {
        var legacy = RemoteRepository.TryParse("https://contoso.visualstudio.com/Proj/_git/Repo")!;

        new Uri(legacy.ApiBase, "_apis/git/repositories/Repo/pullrequests").ToString()
            .ShouldBe("https://contoso.visualstudio.com/Proj/_apis/git/repositories/Repo/pullrequests");
    }
}
