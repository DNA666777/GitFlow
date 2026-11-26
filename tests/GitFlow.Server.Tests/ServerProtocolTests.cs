using System.Text.Json;
using GitFlow.Core.Flow;
using GitFlow.Core.Model;

namespace GitFlow.Server.Tests;

/// <summary>Each JSON-RPC method end to end, on real temporary repositories (texts asked in French).</summary>
public class ServerProtocolTests
{
    [Fact]
    public async Task Initialize_sets_the_language_of_the_messages()
    {
        await using var s = await ServerSession.CreateAsync(initialized: false);

        var info = await s.CallAsync<JsonElement>("initialize", new { culture = "fr-FR" });

        info.GetProperty("culture").GetString().ShouldBe("fr-FR");
        info.GetProperty("version").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Wire_format_is_camel_case_with_enums_as_strings()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "login" });

        var state = await s.CallAsync<JsonElement>("getState", new { repository = s.Repo.WorkDir });

        state.GetProperty("isInitialized").GetBoolean().ShouldBeTrue();
        state.GetProperty("currentBranch").GetString().ShouldBe("feature/login");
        state.GetProperty("currentFlowBranch").GetProperty("type").GetString().ShouldBe("feature");
        state.GetProperty("naming").GetProperty("developBranch").GetString().ShouldBe("develop");
        state.GetProperty("flowBranches").EnumerateArray().Select(b => b.GetProperty("fullName").GetString()).ShouldBe(["feature/login"]);
    }

    [Fact]
    public async Task Find_repository_and_state_of_an_uninitialized_repository()
    {
        await using var s = await ServerSession.CreateAsync(initialized: false);
        var sub = Directory.CreateDirectory(Path.Combine(s.Repo.WorkDir, "src", "app")).FullName;

        var root = await s.CallAsync<string?>("findRepository", new { path = sub });
        var state = await s.CallAsync<StateDto?>("getState", new { repository = root });
        var outside = await s.CallAsync<string?>("findRepository", new { path = Path.GetTempPath() });

        Path.GetFullPath(root!).ShouldBe(Path.GetFullPath(s.Repo.WorkDir));
        state!.IsInitialized.ShouldBeFalse();
        state.Naming.ShouldBeNull();
        outside.ShouldBeNull();
    }

    [Fact]
    public async Task Init_writes_the_naming_and_the_finish_defaults()
    {
        await using var s = await ServerSession.CreateAsync(initialized: false);
        var settings = new SettingsDto(
            new NamingDto("master", "dev", "feat/", "release/", "hotfix/", "v"),
            new FinishDefaultsDto(PushAfterFinish: true, DeleteRemoteBranch: false, UsePullRequest: false, PruneOnComplete: false, AzureDevOpsServer: false));

        var result = await s.CallAsync<ResultDto>("init", new { repository = s.Repo.WorkDir, settings });
        var reread = await s.CallAsync<SettingsDto>("getSettings", new { repository = s.Repo.WorkDir, scope = "local" });

        result.Success.ShouldBeTrue(result.Message);
        (await s.Repo.LocalBranchesAsync()).ShouldContain("dev");
        reread.ShouldBe(settings with { });
    }

    [Fact]
    public async Task Start_failure_comes_back_as_a_translated_result()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.Repo.WriteFileAsync("README.md", "changed\n");

        var result = await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "x" });

        result.Success.ShouldBeFalse();
        result.Status.ShouldBe(FlowStatus.Failed);
        result.Error.ShouldBe(FlowError.DirtyWorkingTree);
        result.Message.ShouldBe("Des modifications ne sont pas commitées. Faites un commit ou un stash, puis réessayez.");
    }

    [Fact]
    public async Task Feature_start_prepare_finish()
    {
        await using var s = await ServerSession.CreateAsync();
        (await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "login" })).BranchName.ShouldBe("feature/login");
        await s.Repo.CommitFileAsync("login.txt", "login\n", "Ajoute login");

        var prepared = await s.CallAsync<PrepareFinishResponse>("prepareFinish", new { repository = s.Repo.WorkDir, branch = (string?)null });
        var context = prepared.Context!;
        var result = await s.CallAsync<ResultDto>("finish", new
        {
            repository = s.Repo.WorkDir,
            branch = context.Branch.FullName,
            tagMessage = (string?)null,
            pushAfterFinish = false,
            deleteRemoteBranch = false,
        });

        prepared.Error.ShouldBeNull();
        context.Branch.FullName.ShouldBe("feature/login");
        context.Targets.ShouldBe(["develop"]);
        context.TagName.ShouldBeNull();
        context.AsksTagMessage.ShouldBeFalse();
        context.PullRequestAvailable.ShouldBeFalse("origin is a local folder");
        context.PullRequestUnavailableMessage.ShouldStartWith("Mode Pull Request indisponible");
        result.Success.ShouldBeTrue(result.Message);
        result.BranchName.ShouldBe("develop");
        (await s.Repo.LocalBranchesAsync()).ShouldNotContain("feature/login");
    }

    [Fact]
    public async Task Prepare_finish_outside_a_work_branch_gives_the_reason()
    {
        await using var s = await ServerSession.CreateAsync();

        var prepared = await s.CallAsync<PrepareFinishResponse>("prepareFinish", new { repository = s.Repo.WorkDir, branch = (string?)null });

        prepared.Context.ShouldBeNull();
        prepared.Error!.Error.ShouldBe(FlowError.NotAFlowBranch);
        prepared.Error.Message.ShouldBe("« master » n'est pas une branche feature, release ou hotfix.");
    }

    [Fact]
    public async Task Release_with_version_suggestion_tag_and_push()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.Repo.GitAsync("switch", "develop");
        await s.Repo.CommitFileAsync("a.txt", "a\n", "Ajoute a");

        (await s.CallAsync<int?>("countUnreleasedCommits", new { repository = s.Repo.WorkDir })).ShouldBe(1);
        var version = await s.CallAsync<string>("suggestVersion", new { repository = s.Repo.WorkDir, type = "release" });
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "release", name = version });
        var context = (await s.CallAsync<PrepareFinishResponse>("prepareFinish", new { repository = s.Repo.WorkDir, branch = (string?)null })).Context!;
        var result = await s.CallAsync<ResultDto>("finish", new
        {
            repository = s.Repo.WorkDir,
            branch = context.Branch.FullName,
            tagMessage = "Version " + version,
            pushAfterFinish = true,
            deleteRemoteBranch = false,
        });

        context.Targets.ShouldBe(["master", "develop"]);
        context.TagName.ShouldBe(version);
        context.DefaultTagMessage.ShouldBe("Release " + version);
        context.AsksTagMessage.ShouldBeTrue();
        result.Success.ShouldBeTrue(result.Message);
        result.TagName.ShouldBe(version);
        (await s.Repo.GitInAsync(s.Repo.OriginDir, "tag", "--list")).ShouldBe(version);
        (await s.Repo.GitAsync("tag", "-n1", "--list", version)).ShouldEndWith("Version " + version);
    }

    [Fact]
    public async Task Conflict_then_resume()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "x" });
        await s.Repo.CommitFileAsync("README.md", "feature\n", "Feature");
        await s.Repo.GitAsync("switch", "develop");
        await s.Repo.CommitFileAsync("README.md", "develop\n", "Develop");

        var first = await s.CallAsync<ResultDto>("finish", new { repository = s.Repo.WorkDir, branch = "feature/x", tagMessage = (string?)null, pushAfterFinish = false, deleteRemoteBranch = false });
        var resume = (await s.CallAsync<PrepareFinishResponse>("prepareFinish", new { repository = s.Repo.WorkDir, branch = (string?)null })).Context!;
        await s.Repo.WriteFileAsync("README.md", "both\n");
        await s.Repo.GitAsync("add", "README.md");
        var second = await s.CallAsync<ResultDto>("finish", new { repository = s.Repo.WorkDir, branch = "feature/x", tagMessage = (string?)null, pushAfterFinish = false, deleteRemoteBranch = false });

        first.Status.ShouldBe(FlowStatus.Conflict);
        first.ConflictedFiles.ShouldBe(["README.md"]);
        resume.IsResume.ShouldBeTrue();
        resume.PullRequestAvailable.ShouldBeFalse();
        second.Success.ShouldBeTrue(second.Message);
    }

    [Fact]
    public async Task Publish_pushes_the_branch()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "share" });

        var result = await s.CallAsync<ResultDto>("publish", new { repository = s.Repo.WorkDir, branch = (string?)null });

        result.Success.ShouldBeTrue(result.Message);
        (await s.Repo.OriginBranchesAsync()).ShouldContain("feature/share");
    }

    [Fact]
    public async Task Settings_warnings_then_save()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "old" });
        var settings = await s.CallAsync<SettingsDto>("getSettings", new { repository = s.Repo.WorkDir, scope = "local" });
        var proposed = settings.Naming with { FeaturePrefix = "feat/" };

        var warnings = await s.CallAsync<IReadOnlyList<string>>("getSettingsWarnings", new { repository = s.Repo.WorkDir, proposed });
        var saved = await s.CallAsync<ResultDto>("saveSettings", new { repository = s.Repo.WorkDir, settings = settings with { Naming = proposed }, scope = "local" });
        var branches = await s.CallAsync<IReadOnlyList<string>>("getLocalBranches", new { repository = s.Repo.WorkDir });

        warnings.ShouldHaveSingleItem().ShouldStartWith("1 branche(s) utilisent encore l'ancien préfixe « feature/ »");
        saved.Success.ShouldBeTrue(saved.Message);
        (await s.Repo.GitAsync("config", "--local", "gitflow.prefix.feature")).ShouldBe("feat/");
        branches.ShouldContain("feature/old");
    }

    [Fact]
    public async Task Invalid_settings_are_refused_with_the_reasons()
    {
        await using var s = await ServerSession.CreateAsync();
        var settings = await s.CallAsync<SettingsDto>("getSettings", new { repository = s.Repo.WorkDir, scope = "local" });

        var saved = await s.CallAsync<ResultDto>("saveSettings", new
        {
            repository = s.Repo.WorkDir,
            settings = settings with { Naming = settings.Naming with { DevelopBranch = "master" } },
            scope = "local",
        });

        saved.Success.ShouldBeFalse();
        saved.Error.ShouldBe(FlowError.InvalidConfig);
        saved.Message.ShouldContain("doivent être différentes");
    }

    [Fact]
    public async Task Every_git_command_is_sent_to_the_client_log()
    {
        await using var s = await ServerSession.CreateAsync();

        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "logged" });

        await WaitUntilAsync(() => s.Logs.Any(l => l.Contains("git switch -c feature/logged", StringComparison.Ordinal) || l.Contains("feature/logged", StringComparison.Ordinal)));
        s.Logs.ShouldAllBe(l => l.StartsWith('['));
    }

    [Fact]
    public async Task Unknown_method_is_a_json_rpc_error()
    {
        await using var s = await ServerSession.CreateAsync(initialized: false);

        await Should.ThrowAsync<StreamJsonRpc.RemoteMethodNotFoundException>(() => s.CallAsync<JsonElement>("doesNotExist", new { }));
    }

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(50);
        }

        condition().ShouldBeTrue();
    }
}
