using System.Collections.Concurrent;
using System.Text.Json;
using GitFlow.Core.IntegrationTests;
using GitFlow.Core.PullRequests;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace GitFlow.Server.Tests;

/// <summary>
/// A real JSON-RPC connection (same framing and JSON options as the VS Code extension) between a client
/// and <see cref="GitFlowRpc"/>, on a temporary repository with a bare origin.
/// </summary>
internal sealed class ServerSession : IAsyncDisposable
{
    public const string GitHubUrl = "https://github.com/test/repo.git";

    private readonly JsonRpc _server;

    private ServerSession(TestRepository repo, FakePlatform? platform)
    {
        Repo = repo;
        Platform = platform;
        var (serverStream, clientStream) = FullDuplexStream.CreatePair();
        _server = RpcHost.Create(
            serverStream,
            serverStream,
            log => new Core.Git.GitCliRunner(log, new Core.Git.GitCliRunnerOptions
            {
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["GIT_CONFIG_GLOBAL"] = Path.Combine(repo.Root, "global.gitconfig"),
                    ["GIT_CONFIG_NOSYSTEM"] = "1",
                },
            }),
            runner => platform is null
                ? new PullRequestService(runner, new HttpClient())
                : new PullRequestService(runner, [platform]));
        _server.StartListening();

        var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcHost.JsonOptions };
        Client = new JsonRpc(new HeaderDelimitedMessageHandler(clientStream, clientStream, formatter));
        Client.AddLocalRpcMethod("log", new Action<string>(line => Logs.Enqueue(line)));
        Client.StartListening();
    }

    public TestRepository Repo { get; }

    public FakePlatform? Platform { get; }

    public JsonRpc Client { get; }

    public ConcurrentQueue<string> Logs { get; } = new();

    /// <param name="initialized">Run "init" with the default naming.</param>
    /// <param name="pullRequests">origin "is" GitHub (rewritten to the local bare repository) with an in-memory platform.</param>
    public static async Task<ServerSession> CreateAsync(bool initialized = true, bool pullRequests = false)
    {
        var repo = await TestRepository.CreateAsync();
        FakePlatform? platform = null;
        if (pullRequests)
        {
            await repo.GitAsync("config", "--global", $"url.{repo.OriginDir.Replace('\\', '/')}.insteadOf", GitHubUrl);
            await repo.GitAsync("remote", "set-url", "origin", GitHubUrl);
            var store = Path.Combine(repo.Root, "credentials-store");
            await File.WriteAllTextAsync(store, "https://tester:fake-token@github.com\n");
            await repo.GitAsync("config", "--global", "credential.helper", $"store --file={store.Replace('\\', '/')}");
            platform = new FakePlatform();
        }

        var session = new ServerSession(repo, platform);
        await session.CallAsync<JsonElement>("initialize", new { culture = "fr-FR" });
        if (initialized)
        {
            var settings = await session.CallAsync<SettingsDto>("getSettings", new { repository = repo.WorkDir, scope = "local" });
            var init = await session.CallAsync<ResultDto>("init", new { repository = repo.WorkDir, settings });
            init.Success.ShouldBeTrue(init.Message);
            await repo.GitAsync("push", "origin", "develop");
        }

        return session;
    }

    public Task<T> CallAsync<T>(string method, object arguments) =>
        Client.InvokeWithParameterObjectAsync<T>(method, arguments);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _server.Dispose();
        await Repo.DisposeAsync();
    }

    /// <summary>In-memory GitHub: records the pull requests, never merges anything by itself.</summary>
    internal sealed class FakePlatform : IPullRequestProvider
    {
        private readonly List<PullRequestInfo> _prs = [];

        public HostingPlatform Platform => HostingPlatform.GitHub;

        public IReadOnlyList<PullRequestInfo> PullRequests => _prs;

        public Task<PullRequestInfo> CreateAsync(RemoteRepository repository, HostCredential credential, PullRequestDraft draft, CancellationToken ct)
        {
            var id = _prs.Count + 1;
            var pr = new PullRequestInfo(id, new Uri($"https://github.com/test/repo/pull/{id}"), PullRequestState.Open, draft.Source, draft.Target, draft.Title, null);
            _prs.Add(pr);
            return Task.FromResult(pr);
        }

        public Task<PullRequestInfo?> FindAsync(RemoteRepository repository, HostCredential credential, string source, string target, CancellationToken ct) =>
            Task.FromResult(_prs.LastOrDefault(p => p.Source == source && p.Target == target));

        public Task<PullRequestInfo?> GetAsync(RemoteRepository repository, HostCredential credential, int id, CancellationToken ct) =>
            Task.FromResult(_prs.FirstOrDefault(p => p.Id == id));
    }
}
