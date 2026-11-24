using GitFlow.Core.Config;
using GitFlow.Core.Flow;
using GitFlow.Core.Model;
using Shouldly;

namespace GitFlow.Core.IntegrationTests;

public class FlowIntegrationTests
{
    private static async Task<TestRepository> CreateInitializedAsync(bool withOrigin = true, GitFlowConfig? config = null)
    {
        config ??= GitFlowConfig.Default;
        var repo = await TestRepository.CreateAsync(withOrigin, config.MasterBranch);
        var init = await repo.Flow.InitAsync(repo.WorkDir, config);
        init.IsSuccess.ShouldBeTrue(init.Detail);
        return repo;
    }

    // ---------- Init ----------

    [Fact]
    public async Task Init_creates_develop_from_master_and_writes_config()
    {
        await using var repo = await TestRepository.CreateAsync();

        var result = await repo.Flow.InitAsync(repo.WorkDir, GitFlowConfig.Default);

        result.IsSuccess.ShouldBeTrue();
        (await repo.LocalBranchesAsync()).ShouldBe(["develop", "master"], ignoreOrder: true);
        (await repo.GitAsync("rev-parse", "develop")).ShouldBe(await repo.GitAsync("rev-parse", "master"));
        (await repo.GitAsync("config", "--local", "gitflow.branch.develop")).ShouldBe("develop");
        (await repo.GitAsync("config", "--local", "gitflow.prefix.feature")).ShouldBe("feature/");
        (await repo.TryGitAsync("config", "--local", "--get", "gitflow.prefix.versiontag")).StandardOutput.Trim().ShouldBe("");

        var state = await repo.Flow.GetStateAsync(repo.WorkDir);
        state!.IsInitialized.ShouldBeTrue();
        state.Config.ShouldBe(GitFlowConfig.Default);
    }

    [Fact]
    public async Task Init_reuses_existing_git_flow_config()
    {
        await using var repo = await TestRepository.CreateAsync(masterBranch: "main");
        await repo.GitAsync("config", "gitflow.branch.master", "main");
        await repo.GitAsync("config", "gitflow.branch.develop", "dev");
        await repo.GitAsync("config", "gitflow.prefix.feature", "feat/");
        await repo.GitAsync("config", "gitflow.prefix.release", "rel/");
        await repo.GitAsync("config", "gitflow.prefix.hotfix", "fix/");
        await repo.GitAsync("config", "gitflow.prefix.versiontag", "v");

        var defaults = await repo.Flow.ConfigStore.GetInitDefaultsAsync(repo.WorkDir);

        defaults.ShouldBe(new GitFlowConfig("main", "dev", "feat/", "rel/", "fix/", "v"));
        (await repo.Flow.InitAsync(repo.WorkDir, defaults)).IsSuccess.ShouldBeTrue();
        (await repo.LocalBranchesAsync()).ShouldContain("dev");
    }

    [Fact]
    public async Task Init_defaults_come_from_global_config()
    {
        await using var repo = await TestRepository.CreateAsync();
        await repo.Flow.ConfigStore.WriteAsync(repo.WorkDir, GitFlowConfig.Default with { VersionTagPrefix = "v" }, ConfigScope.Global);

        (await repo.Flow.ConfigStore.GetInitDefaultsAsync(repo.WorkDir)).VersionTagPrefix.ShouldBe("v");
        (await repo.Flow.ConfigStore.ReadAsync(repo.WorkDir, ConfigScope.Local)).ShouldBeNull();
    }

    [Fact]
    public async Task Not_initialized_repository_is_refused()
    {
        await using var repo = await TestRepository.CreateAsync();

        var result = await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");

        result.Error.ShouldBe(FlowError.NotInitialized);
        (await repo.LocalBranchesAsync()).ShouldBe(["master"]);
    }

    [Fact]
    public async Task Init_validation_uses_git_rules()
    {
        await using var repo = await TestRepository.CreateAsync();

        var result = await repo.Flow.InitAsync(repo.WorkDir, GitFlowConfig.Default with { FeaturePrefix = "fea ture/", VersionTagPrefix = "v~" });

        result.Error.ShouldBe(FlowError.InvalidConfig);
        result.ConfigErrors.Select(e => e.Kind).ShouldBe(
            [ConfigValidationErrorKind.InvalidPrefix, ConfigValidationErrorKind.InvalidTagPrefix], ignoreOrder: true);
        (await repo.Flow.ConfigStore.ReadAsync(repo.WorkDir, ConfigScope.Local)).ShouldBeNull();
    }

    // ---------- Feature ----------

    [Fact]
    public async Task Feature_start_and_finish()
    {
        await using var repo = await CreateInitializedAsync();

        var start = await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "login");
        start.IsSuccess.ShouldBeTrue();
        (await repo.CurrentBranchAsync()).ShouldBe("feature/login");
        var work = await repo.CommitFileAsync("login.txt", "login\n", "Add login");
        var masterBefore = await repo.GitAsync("rev-parse", "master");

        var finish = await repo.Flow.FinishAsync(repo.WorkDir);

        finish.IsSuccess.ShouldBeTrue();
        (await repo.CurrentBranchAsync()).ShouldBe("develop");
        (await repo.LocalBranchesAsync()).ShouldNotContain("feature/login");
        (await repo.IsAncestorAsync(work, "develop")).ShouldBeTrue();
        (await repo.ParentsAsync("develop")).Length.ShouldBe(2, "--no-ff merge commit expected");
        (await repo.GitAsync("log", "-1", "--format=%s", "develop")).ShouldBe("Merge branch 'feature/login' into develop");
        (await repo.GitAsync("rev-parse", "master")).ShouldBe(masterBefore, "features never touch master");
        (await repo.GitAsync("tag", "--list")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Feature_without_commits_is_finished_cleanly()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "empty");

        var finish = await repo.Flow.FinishAsync(repo.WorkDir);

        finish.IsSuccess.ShouldBeTrue();
        (await repo.LocalBranchesAsync()).ShouldBe(["develop", "master"], ignoreOrder: true);
    }

    // ---------- Release ----------

    [Fact]
    public async Task Release_start_and_finish()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "a");
        var featureCommit = await repo.CommitFileAsync("a.txt", "a\n", "Feature a");
        await repo.Flow.FinishAsync(repo.WorkDir);

        (await repo.Flow.SuggestVersionAsync(repo.WorkDir, BranchType.Release)).ShouldBe("0.1.0");
        var start = await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.0.0");
        start.BranchName.ShouldBe("release/1.0.0");
        (await repo.Flow.GetStateAsync(repo.WorkDir))!.ShowReleaseReminder.ShouldBeTrue();
        var fix = await repo.CommitFileAsync("version.txt", "1.0.0\n", "Bump version");

        var finish = await repo.Flow.FinishAsync(repo.WorkDir);

        finish.IsSuccess.ShouldBeTrue();
        finish.TagName.ShouldBe("1.0.0");
        (await repo.CurrentBranchAsync()).ShouldBe("develop");
        (await repo.LocalBranchesAsync()).ShouldBe(["develop", "master"], ignoreOrder: true);
        (await repo.IsAncestorAsync(featureCommit, "master")).ShouldBeTrue();
        (await repo.IsAncestorAsync(fix, "master")).ShouldBeTrue();
        (await repo.IsAncestorAsync(fix, "develop")).ShouldBeTrue();
        (await repo.ParentsAsync("master")).Length.ShouldBe(2);
        (await repo.GitAsync("cat-file", "-t", "1.0.0")).ShouldBe("tag", "annotated tag expected");
        (await repo.GitAsync("rev-parse", "1.0.0^{commit}")).ShouldBe(await repo.GitAsync("rev-parse", "master"));
        (await repo.GitAsync("tag", "-l", "--format=%(contents:subject)", "1.0.0")).ShouldBe("Release 1.0.0");

        (await repo.Flow.SuggestVersionAsync(repo.WorkDir, BranchType.Release)).ShouldBe("1.1.0");
        (await repo.Flow.SuggestVersionAsync(repo.WorkDir, BranchType.Hotfix)).ShouldBe("1.0.1");
    }

    [Fact]
    public async Task Unreleased_commits_detect_an_empty_release()
    {
        await using var repo = await CreateInitializedAsync();
        (await repo.Flow.CountUnreleasedCommitsAsync(repo.WorkDir)).ShouldBe(0, "fresh develop = master");

        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "a");
        await repo.CommitFileAsync("a.txt", "a\n", "A1");
        await repo.CommitFileAsync("a.txt", "a2\n", "A2");
        await repo.Flow.FinishAsync(repo.WorkDir);
        (await repo.Flow.CountUnreleasedCommitsAsync(repo.WorkDir)).ShouldBe(2, "feature commits, not the merge commit");

        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.0.0");
        await repo.CommitFileAsync("version.txt", "1.0.0\n", "Bump");
        await repo.Flow.FinishAsync(repo.WorkDir);
        (await repo.Flow.CountUnreleasedCommitsAsync(repo.WorkDir)).ShouldBe(0, "only the release merge into develop remains");

        // Empty feature: nothing new either.
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "empty");
        await repo.Flow.FinishAsync(repo.WorkDir);
        (await repo.Flow.CountUnreleasedCommitsAsync(repo.WorkDir)).ShouldBe(0);
    }

    [Fact]
    public async Task Release_with_existing_tag_is_refused()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.GitAsync("tag", "1.0.0", "master");

        var start = await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.0.0");

        start.Error.ShouldBe(FlowError.TagAlreadyExists);
    }

    // ---------- Hotfix ----------

    [Fact]
    public async Task Hotfix_start_and_finish()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.0.0");
        await repo.Flow.FinishAsync(repo.WorkDir);
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "wip");
        var unreleased = await repo.CommitFileAsync("wip.txt", "wip\n", "Unreleased work");
        await repo.Flow.FinishAsync(repo.WorkDir);

        var version = await repo.Flow.SuggestVersionAsync(repo.WorkDir, BranchType.Hotfix);
        version.ShouldBe("1.0.1");
        var start = await repo.Flow.StartAsync(repo.WorkDir, BranchType.Hotfix, version);
        start.IsSuccess.ShouldBeTrue();
        (await repo.IsAncestorAsync(unreleased, "HEAD")).ShouldBeFalse("hotfix starts from master");
        var fix = await repo.CommitFileAsync("fix.txt", "fix\n", "Fix prod");

        var finish = await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { TagMessage = "Correctif de prod" });

        finish.IsSuccess.ShouldBeTrue();
        (await repo.IsAncestorAsync(fix, "master")).ShouldBeTrue();
        (await repo.IsAncestorAsync(fix, "develop")).ShouldBeTrue();
        (await repo.IsAncestorAsync(unreleased, "master")).ShouldBeFalse();
        (await repo.GitAsync("tag", "-l", "--format=%(contents:subject)", "1.0.1")).ShouldBe("Correctif de prod");
        (await repo.LocalBranchesAsync()).ShouldNotContain("hotfix/1.0.1");
    }

    [Fact]
    public async Task Hotfix_with_release_in_progress_is_merged_into_the_release()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.1.0");
        await repo.CommitFileAsync("rel.txt", "rel\n", "Release prep");
        var developBefore = await repo.GitAsync("rev-parse", "develop");
        await repo.GitAsync("switch", "master");

        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Hotfix, "1.0.1");
        var fix = await repo.CommitFileAsync("fix.txt", "fix\n", "Fix prod");
        var finish = await repo.Flow.FinishAsync(repo.WorkDir);

        finish.IsSuccess.ShouldBeTrue();
        finish.BranchName.ShouldBe("release/1.1.0");
        (await repo.IsAncestorAsync(fix, "master")).ShouldBeTrue();
        (await repo.IsAncestorAsync(fix, "release/1.1.0")).ShouldBeTrue();
        (await repo.GitAsync("rev-parse", "develop")).ShouldBe(developBefore);

        // The fix reaches develop when the release is finished.
        (await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { BranchName = "release/1.1.0" })).IsSuccess.ShouldBeTrue();
        (await repo.IsAncestorAsync(fix, "develop")).ShouldBeTrue();
    }

    // ---------- Safety ----------

    [Fact]
    public async Task Dirty_working_tree_blocks_start_and_finish()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        await repo.WriteFileAsync("README.md", "changed\n");

        (await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "y")).Error.ShouldBe(FlowError.DirtyWorkingTree);
        (await repo.Flow.FinishAsync(repo.WorkDir)).Error.ShouldBe(FlowError.DirtyWorkingTree);
        (await repo.CurrentBranchAsync()).ShouldBe("feature/x");
        (await File.ReadAllTextAsync(Path.Combine(repo.WorkDir, "README.md"))).ShouldBe("changed\n");
        (await repo.GitAsync("stash", "list")).ShouldBeEmpty("never stash silently");
    }

    [Fact]
    public async Task Untracked_files_do_not_block()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.WriteFileAsync("notes.txt", "local notes\n");

        (await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x")).IsSuccess.ShouldBeTrue();
        File.Exists(Path.Combine(repo.WorkDir, "notes.txt")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("bad..name")]
    [InlineData("bad~name")]
    [InlineData("x:y")]
    [InlineData("x.lock")]
    public async Task Invalid_names_are_refused(string name)
    {
        await using var repo = await CreateInitializedAsync();

        var result = await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, name);

        result.Error.ShouldBe(FlowError.InvalidBranchName);
        (await repo.LocalBranchesAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Existing_branch_is_refused()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        await repo.GitAsync("switch", "develop");

        var result = await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");

        result.Error.ShouldBe(FlowError.BranchAlreadyExists);
        (await repo.CurrentBranchAsync()).ShouldBe("develop");
    }

    [Fact]
    public async Task Conflict_stops_then_finish_resumes_after_resolution()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        var featureCommit = await repo.CommitFileAsync("README.md", "feature version\n", "Feature edit");
        await repo.GitAsync("switch", "develop");
        await repo.CommitFileAsync("README.md", "develop version\n", "Develop edit");
        await repo.GitAsync("switch", "feature/x");

        var first = await repo.Flow.FinishAsync(repo.WorkDir);

        first.Status.ShouldBe(FlowStatus.Conflict);
        first.Conflict!.TargetBranch.ShouldBe("develop");
        first.Conflict.Files.ShouldBe(["README.md"]);
        (await repo.LocalBranchesAsync()).ShouldContain("feature/x", "nothing is deleted on conflict");
        (await repo.TryGitAsync("rev-parse", "--verify", "--quiet", "MERGE_HEAD")).Success.ShouldBeTrue("repo is left in merge state");
        (await repo.Flow.GetStateAsync(repo.WorkDir))!.PendingFinish!.FullName.ShouldBe("feature/x");

        // Not resolved yet: Finish reports the conflict again and changes nothing.
        (await repo.Flow.FinishAsync(repo.WorkDir)).Status.ShouldBe(FlowStatus.Conflict);

        await repo.WriteFileAsync("README.md", "merged version\n");
        await repo.GitAsync("add", "README.md");
        var resumed = await repo.Flow.FinishAsync(repo.WorkDir);

        resumed.IsSuccess.ShouldBeTrue();
        (await repo.LocalBranchesAsync()).ShouldNotContain("feature/x");
        (await repo.IsAncestorAsync(featureCommit, "develop")).ShouldBeTrue();
        (await repo.ParentsAsync("develop")).Length.ShouldBe(2);
        (await File.ReadAllTextAsync(Path.Combine(repo.WorkDir, "README.md"))).ShouldBe("merged version\n");
    }

    [Fact]
    public async Task Release_conflict_on_develop_resumes_without_retagging()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.0.0");
        var releaseCommit = await repo.CommitFileAsync("README.md", "release\n", "Release edit");
        await repo.GitAsync("switch", "develop");
        await repo.CommitFileAsync("README.md", "develop\n", "Develop edit");

        var first = await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { BranchName = "release/1.0.0" });

        first.Status.ShouldBe(FlowStatus.Conflict);
        first.Conflict!.TargetBranch.ShouldBe("develop");
        var tagCommit = await repo.GitAsync("rev-parse", "1.0.0^{commit}");
        tagCommit.ShouldBe(await repo.GitAsync("rev-parse", "master"));

        await repo.WriteFileAsync("README.md", "resolved\n");
        await repo.GitAsync("add", "README.md");
        var resumed = await repo.Flow.FinishAsync(repo.WorkDir);

        resumed.IsSuccess.ShouldBeTrue();
        (await repo.GitAsync("rev-parse", "1.0.0^{commit}")).ShouldBe(tagCommit);
        (await repo.IsAncestorAsync(releaseCommit, "develop")).ShouldBeTrue();
        (await repo.LocalBranchesAsync()).ShouldNotContain("release/1.0.0");
    }

    [Fact]
    public async Task Merge_concluded_by_hand_is_detected_and_finish_completes_it()
    {
        // Same scenario as the manual test: conflict, the IDE commits the merge itself.
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Hotfix, "0.0.1");
        await repo.CommitFileAsync("README.md", "hotfix\n", "Hotfix edit");
        await repo.GitAsync("switch", "develop");
        await repo.CommitFileAsync("README.md", "develop\n", "Develop edit");

        (await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { BranchName = "hotfix/0.0.1" })).Status.ShouldBe(FlowStatus.Conflict);
        await repo.WriteFileAsync("README.md", "resolved\n");
        await repo.GitAsync("add", "README.md");
        await repo.GitAsync("commit", "--no-edit");

        var state = await repo.Flow.GetStateAsync(repo.WorkDir);
        state!.IsMergeInProgress.ShouldBeFalse();
        state.MergedFlowBranches.ShouldBe(["hotfix/0.0.1"]);

        var finish = await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { BranchName = "hotfix/0.0.1" });

        finish.IsSuccess.ShouldBeTrue();
        (await repo.LocalBranchesAsync()).ShouldBe(["develop", "master"], ignoreOrder: true);
        (await repo.GitAsync("tag", "--list")).ShouldBe("0.0.1");
    }

    [Fact]
    public async Task Conflict_can_be_aborted_by_the_user()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        await repo.CommitFileAsync("README.md", "a\n", "A");
        await repo.GitAsync("switch", "develop");
        await repo.CommitFileAsync("README.md", "b\n", "B");

        (await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { BranchName = "feature/x" })).Status.ShouldBe(FlowStatus.Conflict);
        await repo.GitAsync("merge", "--abort");

        var state = await repo.Flow.GetStateAsync(repo.WorkDir);
        state!.IsMergeInProgress.ShouldBeFalse();
        state.IsDirty.ShouldBeFalse();
        state.FlowBranches.Select(b => b.FullName).ShouldBe(["feature/x"]);
    }

    // ---------- Remote ----------

    [Fact]
    public async Task Repository_without_remote()
    {
        await using var repo = await CreateInitializedAsync(withOrigin: false);
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x", new StartOptions { FetchFromRemote = true });
        await repo.CommitFileAsync("x.txt", "x\n", "X");

        (await repo.Flow.PublishAsync(repo.WorkDir)).Error.ShouldBe(FlowError.NoRemote);
        (await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { PushAfterFinish = true })).Error.ShouldBe(FlowError.NoRemote);
        (await repo.Flow.FinishAsync(repo.WorkDir)).IsSuccess.ShouldBeTrue();
        (await repo.Flow.GetStateAsync(repo.WorkDir))!.HasRemote.ShouldBeFalse();
    }

    [Fact]
    public async Task Publish_then_finish_with_push_and_remote_delete()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "2.0.0");
        await repo.CommitFileAsync("v.txt", "2\n", "Bump");

        (await repo.Flow.PublishAsync(repo.WorkDir)).IsSuccess.ShouldBeTrue();
        (await repo.OriginBranchesAsync()).ShouldContain("release/2.0.0");
        (await repo.GitAsync("rev-parse", "--abbrev-ref", "release/2.0.0@{upstream}")).ShouldBe("origin/release/2.0.0");

        var finish = await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { PushAfterFinish = true, DeleteRemoteBranch = true });

        finish.IsSuccess.ShouldBeTrue();
        finish.Warnings.ShouldBeEmpty();
        (await repo.OriginBranchesAsync()).ShouldBe(["develop", "master"], ignoreOrder: true);
        (await repo.GitInAsync(repo.OriginDir, "tag", "--list")).ShouldBe("2.0.0");
        (await repo.GitInAsync(repo.OriginDir, "rev-parse", "master")).ShouldBe(await repo.GitAsync("rev-parse", "master"));
        (await repo.GitInAsync(repo.OriginDir, "rev-parse", "develop")).ShouldBe(await repo.GitAsync("rev-parse", "develop"));
    }

    [Fact]
    public async Task Start_with_fetch_starts_from_up_to_date_develop()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.GitAsync("push", "origin", "develop");
        // Another developer pushes to develop.
        var other = Path.Combine(repo.Root, "other");
        await repo.GitInAsync(repo.Root, "clone", "-b", "develop", repo.OriginDir, other);
        await repo.GitInAsync(other, "config", "user.name", "Other");
        await repo.GitInAsync(other, "config", "user.email", "other@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(other, "other.txt"), "other\n");
        await repo.GitInAsync(other, "add", "other.txt");
        await repo.GitInAsync(other, "commit", "-m", "Other work");
        await repo.GitInAsync(other, "push", "origin", "develop");
        var remoteDevelop = await repo.GitInAsync(other, "rev-parse", "HEAD");

        var start = await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x", new StartOptions { FetchFromRemote = true });

        start.IsSuccess.ShouldBeTrue();
        (await repo.IsAncestorAsync(remoteDevelop, "feature/x")).ShouldBeTrue();
        (await repo.GitAsync("rev-parse", "develop")).ShouldBe(remoteDevelop);
    }

    // ---------- Naming ----------

    [Fact]
    public async Task Custom_naming_main_dev_feat_and_no_tag_prefix()
    {
        var config = new GitFlowConfig("main", "dev", "feat/", "rel/", "fix/", "");
        await using var repo = await CreateInitializedAsync(config: config);

        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        (await repo.CurrentBranchAsync()).ShouldBe("feat/x");
        await repo.CommitFileAsync("x.txt", "x\n", "X");
        (await repo.Flow.FinishAsync(repo.WorkDir)).BranchName.ShouldBe("dev");

        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.0.0");
        (await repo.CurrentBranchAsync()).ShouldBe("rel/1.0.0");
        var release = await repo.Flow.FinishAsync(repo.WorkDir);

        release.TagName.ShouldBe("1.0.0");
        (await repo.LocalBranchesAsync()).ShouldBe(["dev", "main"], ignoreOrder: true);
        (await repo.GitAsync("rev-parse", "1.0.0^{commit}")).ShouldBe(await repo.GitAsync("rev-parse", "main"));
    }

    [Fact]
    public async Task Tag_prefix_v()
    {
        await using var repo = await CreateInitializedAsync(config: GitFlowConfig.Default with { VersionTagPrefix = "v" });
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.0.0");

        var finish = await repo.Flow.FinishAsync(repo.WorkDir);

        finish.TagName.ShouldBe("v1.0.0");
        (await repo.Flow.SuggestVersionAsync(repo.WorkDir, BranchType.Hotfix)).ShouldBe("1.0.1");
    }

    // ---------- Commit ----------

    [Fact]
    public async Task Commit_all_changes_with_generated_message()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "ABC-12-login");
        await repo.WriteFileAsync("README.md", "changed\nline 2\n");
        Directory.CreateDirectory(Path.Combine(repo.WorkDir, "src"));
        await repo.WriteFileAsync(Path.Combine("src", "Login é.cs"), "class Login {}\n");
        var commits = new CommitService(repo.Runner);

        var preview = await commits.GetPreviewAsync(repo.WorkDir);

        preview!.Mode.ShouldBe(CommitMode.AllChanges);
        preview.CanCommit.ShouldBeTrue();
        preview.SuggestedMessage.ShouldBe(string.Join(Environment.NewLine,
            "Met à jour 2 fichiers",
            "",
            "Ajoutés (1) :",
            "- src/Login é.cs",
            "Modifiés (1) :",
            "- README.md (+2 -1)",
            "",
            "Réf. : ABC-12"));

        var result = await commits.CommitAsync(repo.WorkDir, preview.SuggestedMessage);

        result.IsSuccess.ShouldBeTrue();
        (await repo.GitAsync("status", "--porcelain")).ShouldBeEmpty();
        (await repo.GitAsync("log", "-1", "--format=%B")).ShouldBe(preview.SuggestedMessage.Replace(Environment.NewLine, "\n"));
        (await repo.GitAsync("log", "-1", "--format=%s")).ShouldBe("Met à jour 2 fichiers");
        (await repo.CurrentBranchAsync()).ShouldBe("feature/ABC-12-login");
    }

    [Fact]
    public async Task Commit_respects_staging_and_keeps_hash_lines()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        await repo.WriteFileAsync("a.txt", "a\n");
        await repo.WriteFileAsync("b.txt", "b\n");
        await repo.GitAsync("add", "a.txt");
        var commits = new CommitService(repo.Runner);

        var preview = await commits.GetPreviewAsync(repo.WorkDir);
        preview!.Mode.ShouldBe(CommitMode.StagedOnly);
        preview.Changes.Select(c => c.Path).ShouldBe(["a.txt"]);

        (await commits.CommitAsync(repo.WorkDir, "Ajoute a\n\n#42 lié")).IsSuccess.ShouldBeTrue();

        (await repo.GitAsync("log", "-1", "--format=%B")).ShouldBe("Ajoute a\n\n#42 lié");
        (await repo.GitAsync("show", "--name-only", "--format=", "HEAD")).ShouldBe("a.txt");
        (await repo.GitAsync("status", "--porcelain")).ShouldBe("?? b.txt", "unstaged file left untouched");
    }

    [Fact]
    public async Task Commit_on_master_is_refused_and_changes_are_kept()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.GitAsync("switch", "master");
        await repo.WriteFileAsync("README.md", "on master\n");

        var result = await new CommitService(repo.Runner).CommitAsync(repo.WorkDir, "msg");

        result.Error.ShouldBe(FlowError.CommitOnMasterRefused);
        (await repo.GitAsync("status", "--porcelain")).ShouldBe("M README.md");
        (await repo.GitAsync("rev-list", "--count", "master")).ShouldBe("1");
    }

    [Fact]
    public async Task Commit_during_finish_conflict_is_refused()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");
        await repo.CommitFileAsync("README.md", "a\n", "A");
        await repo.GitAsync("switch", "develop");
        await repo.CommitFileAsync("README.md", "b\n", "B");
        (await repo.Flow.FinishAsync(repo.WorkDir, new FinishOptions { BranchName = "feature/x" })).Status.ShouldBe(FlowStatus.Conflict);
        await repo.WriteFileAsync("README.md", "resolved\n");
        await repo.GitAsync("add", "README.md");

        var result = await new CommitService(repo.Runner).CommitAsync(repo.WorkDir, "msg");

        result.Error.ShouldBe(FlowError.MergeInProgress);
        (await repo.TryGitAsync("rev-parse", "--verify", "--quiet", "MERGE_HEAD")).Success.ShouldBeTrue("the Finish can still be resumed");
    }

    // ---------- Repository discovery ----------

    [Fact]
    public async Task Repository_root_is_found_from_a_file_or_subfolder()
    {
        await using var repo = await CreateInitializedAsync();
        var sub = Directory.CreateDirectory(Path.Combine(repo.WorkDir, "src", "app")).FullName;
        var file = Path.Combine(sub, "App.sln");
        await File.WriteAllTextAsync(file, "");

        (await repo.Flow.FindRepositoryRootAsync(file)).ShouldBe(Path.GetFullPath(repo.WorkDir));
        (await repo.Flow.FindRepositoryRootAsync(sub)).ShouldBe(Path.GetFullPath(repo.WorkDir));
        (await repo.Flow.FindRepositoryRootAsync(Path.Combine(repo.Root, "missing", "x.sln"))).ShouldBeNull();
    }

    [Fact]
    public async Task Merge_targets_are_exposed_for_confirmation()
    {
        await using var repo = await CreateInitializedAsync();
        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Release, "1.0.0");
        var config = GitFlowConfig.Default;

        (await repo.Flow.GetMergeTargetsAsync(repo.WorkDir, config, new FlowBranch(BranchType.Hotfix, "0.9.1", "hotfix/0.9.1")))
            .ShouldBe(["master", "release/1.0.0"]);
        (await repo.Flow.GetMergeTargetsAsync(repo.WorkDir, config, new FlowBranch(BranchType.Feature, "x", "feature/x")))
            .ShouldBe(["develop"]);
    }

    // ---------- Logging ----------

    [Fact]
    public async Task Every_git_command_is_logged()
    {
        await using var repo = await CreateInitializedAsync();
        var before = repo.Log.Entries.Count;

        await repo.Flow.StartAsync(repo.WorkDir, BranchType.Feature, "x");

        var entries = repo.Log.Entries.Skip(before).ToArray();
        entries.ShouldNotBeEmpty();
        entries.ShouldContain(e => e.Result.CommandLine == "git switch -c feature/x develop" && e.Result.ExitCode == 0);
        entries.ShouldAllBe(e => e.WorkingDirectory == repo.WorkDir);
    }
}
