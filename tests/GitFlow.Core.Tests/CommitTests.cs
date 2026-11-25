using GitFlow.Core.Flow;
using GitFlow.Core.Git;
using GitFlow.Core.Model;
using GitFlow.Core.Resources;
using Shouldly;

namespace GitFlow.Core.Tests;

[Collection(MessagesCulture.Collection)]
public class CommitTests
{
    private const string Repo = @"C:\repo";

    private static FileChange Change(ChangeKind kind, string path, string? oldPath = null, bool staged = false) =>
        new(kind, path, oldPath, staged, !staged);

    private static IReadOnlyDictionary<string, FileStat> NoStats => new Dictionary<string, FileStat>();

    public CommitTests() => FlowMessages.Culture = MessagesCulture.French;

    // ---------- status / numstat parsing ----------

    [Fact]
    public void Parses_porcelain_status()
    {
        var changes = GitClient.ParseStatus(
            " M src/Program.cs\0A  src/New.cs\0?? notes é.txt\0 D old.txt\0R  src/B.cs\0src/A.cs\0UU conflict.cs\0MM both.cs\0");

        changes.ShouldBe(
        [
            new FileChange(ChangeKind.Modified, "src/Program.cs", null, false, true),
            new FileChange(ChangeKind.Added, "src/New.cs", null, true, false),
            new FileChange(ChangeKind.Added, "notes é.txt", null, false, true),
            new FileChange(ChangeKind.Deleted, "old.txt", null, false, true),
            new FileChange(ChangeKind.Renamed, "src/B.cs", "src/A.cs", true, false),
            new FileChange(ChangeKind.Conflicted, "conflict.cs", null, true, true),
            new FileChange(ChangeKind.Modified, "both.cs", null, true, true),
        ]);
    }

    [Fact]
    public void Parses_numstat_with_renames_and_binaries()
    {
        var stats = GitClient.ParseNumstat("12\t3\tsrc/Program.cs\0-\t-\tlogo.png\0" + "1\t1\t\0src/A.cs\0src/B.cs\0");

        stats["src/Program.cs"].ShouldBe(new FileStat(12, 3));
        stats["logo.png"].ShouldBe(new FileStat(null, null));
        stats["src/B.cs"].ShouldBe(new FileStat(1, 1));
        stats.ContainsKey("src/A.cs").ShouldBeFalse();
    }

    // ---------- message generation ----------

    [Fact]
    public void No_change_gives_empty_message()
    {
        CommitMessageGenerator.Generate([], NoStats, "feature/x").ShouldBeEmpty();
    }

    [Fact]
    public void Single_modified_file()
    {
        var message = CommitMessageGenerator.Generate(
            [Change(ChangeKind.Modified, "SandboxApp/Program.cs")],
            new Dictionary<string, FileStat> { ["SandboxApp/Program.cs"] = new(4, 1) },
            "feature/hello");

        message.ShouldBe(
            "Modifie Program.cs\n\nModifiés (1) :\n- SandboxApp/Program.cs (+4 -1)".Replace("\n", Environment.NewLine));
    }

    [Theory]
    [InlineData(ChangeKind.Added, "Ajoute a.txt")]
    [InlineData(ChangeKind.Deleted, "Supprime a.txt")]
    [InlineData(ChangeKind.Conflicted, "Résout le conflit dans a.txt")]
    public void Single_file_subjects(ChangeKind kind, string subject)
    {
        CommitMessageGenerator.Generate([Change(kind, "a.txt")], NoStats, null).Split(Environment.NewLine)[0].ShouldBe(subject);
    }

    [Fact]
    public void Single_rename()
    {
        CommitMessageGenerator.Generate([Change(ChangeKind.Renamed, "src/B.cs", "src/A.cs")], NoStats, null)
            .ShouldStartWith("Renomme A.cs en B.cs" + Environment.NewLine + Environment.NewLine + "Renommés (1) :" + Environment.NewLine + "- src/A.cs → src/B.cs");
    }

    [Fact]
    public void Multiple_files_same_kind_in_common_folder()
    {
        var message = CommitMessageGenerator.Generate(
            [Change(ChangeKind.Modified, "src/App/b.cs"), Change(ChangeKind.Modified, "src/App/Sub/a.cs")],
            NoStats,
            null);

        message.Split(Environment.NewLine)[0].ShouldBe("Modifie 2 fichiers dans src/App");
    }

    [Fact]
    public void Multiple_kinds_grouped_and_sorted()
    {
        var message = CommitMessageGenerator.Generate(
            [
                Change(ChangeKind.Deleted, "old.txt"),
                Change(ChangeKind.Modified, "z.cs"),
                Change(ChangeKind.Added, "new.cs"),
                Change(ChangeKind.Modified, "a.cs"),
                Change(ChangeKind.Added, "img.png"),
            ],
            new Dictionary<string, FileStat> { ["a.cs"] = new(1, 2), ["img.png"] = new(null, null) },
            null);

        message.ShouldBe(string.Join(Environment.NewLine,
            "Met à jour 5 fichiers",
            "",
            "Ajoutés (2) :",
            "- img.png (binaire)",
            "- new.cs",
            "Modifiés (2) :",
            "- a.cs (+1 -2)",
            "- z.cs",
            "Supprimés (1) :",
            "- old.txt"));
    }

    [Fact]
    public void Long_lists_are_truncated()
    {
        var changes = Enumerable.Range(0, 60).Select(i => Change(ChangeKind.Added, $"f{i:00}.txt")).ToArray();

        var lines = CommitMessageGenerator.Generate(changes, NoStats, null).Split(Environment.NewLine);

        lines.Count(l => l.StartsWith("- ", StringComparison.Ordinal)).ShouldBe(CommitMessageGenerator.MaxListedFiles);
        lines[^1].ShouldBe("… et 10 autre(s) fichier(s)");
    }

    [Fact]
    public void Long_subject_is_truncated()
    {
        var name = new string('x', 100) + ".cs";

        var subject = CommitMessageGenerator.Generate([Change(ChangeKind.Modified, name)], NoStats, null).Split(Environment.NewLine)[0];

        subject.Length.ShouldBe(72);
        subject.ShouldEndWith("…");
    }

    [Theory]
    [InlineData("feature/ABC-123-login", "ABC-123")]
    [InlineData("feature/ABC-123-and-XY2-7", "ABC-123, XY2-7")]
    [InlineData("hotfix/#42-crash", "#42")]
    [InlineData("feature/login", null)]
    [InlineData("release/1.4.0", null)]
    [InlineData("feature/abc-123", null)]
    public void Ticket_references_from_branch_name(string branch, string? expected)
    {
        var message = CommitMessageGenerator.Generate([Change(ChangeKind.Modified, "a.cs")], NoStats, branch);

        if (expected is null)
        {
            message.ShouldNotContain("Réf.");
        }
        else
        {
            message.Split(Environment.NewLine)[^1].ShouldBe("Réf. : " + expected);
        }
    }

    [Fact]
    public void Special_characters_are_kept()
    {
        CommitMessageGenerator.Generate([Change(ChangeKind.Added, "docs/Résumé \"v2\" & co.md")], NoStats, null)
            .ShouldContain("- docs/Résumé \"v2\" & co.md");
    }

    // ---------- CommitService ----------

    private static FakeGitRunner OnFeature(string status)
    {
        var git = FakeGitRunner.Initialized().AddBranch("feature/x");
        git.CurrentBranch = "feature/x";
        git.Status = status;
        return git;
    }

    [Fact]
    public async Task Preview_all_changes_when_nothing_is_staged()
    {
        var git = OnFeature(" M a.cs\0?? b.cs\0");

        var preview = await new CommitService(git).GetPreviewAsync(Repo);

        preview!.Mode.ShouldBe(CommitMode.AllChanges);
        preview.Changes.Count.ShouldBe(2);
        preview.CanCommit.ShouldBeTrue();
        preview.SuggestedMessage.ShouldStartWith("Met à jour 2 fichiers");
        git.Calls.ShouldContain(c => string.Join(' ', c) == "diff --numstat -z -M HEAD");
    }

    [Fact]
    public async Task Preview_only_staged_when_something_is_staged()
    {
        var git = OnFeature("M  a.cs\0 M b.cs\0?? c.cs\0");

        var preview = await new CommitService(git).GetPreviewAsync(Repo);

        preview!.Mode.ShouldBe(CommitMode.StagedOnly);
        preview.Changes.Select(c => c.Path).ShouldBe(["a.cs"]);
        git.Calls.ShouldContain(c => string.Join(' ', c) == "diff --cached --numstat -z -M HEAD");
    }

    [Fact]
    public async Task Commit_all_adds_everything_then_commits()
    {
        var git = OnFeature(" M a.cs\0");

        var result = await new CommitService(git).CommitAsync(Repo, "  Mon message\n\n#42 reste  ");

        result.IsSuccess.ShouldBeTrue();
        git.MutatingCalls.ShouldBe(["add --all", "commit --cleanup=whitespace -m Mon message\n\n#42 reste"]);
    }

    [Fact]
    public async Task Commit_staged_does_not_add()
    {
        var git = OnFeature("M  a.cs\0 M b.cs\0");

        (await new CommitService(git).CommitAsync(Repo, "msg")).IsSuccess.ShouldBeTrue();

        git.MutatingCalls.ShouldBe(["commit --cleanup=whitespace -m msg"]);
    }

    [Fact]
    public async Task Commit_on_master_is_refused()
    {
        var git = OnFeature(" M a.cs\0");
        git.CurrentBranch = "master";

        var result = await new CommitService(git).CommitAsync(Repo, "msg");

        result.Error.ShouldBe(FlowError.CommitOnMasterRefused);
        result.Detail.ShouldBe("master");
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_during_merge_is_refused()
    {
        var git = OnFeature("M  a.cs\0");
        git.Refs["MERGE_HEAD"] = "f1";

        (await new CommitService(git).CommitAsync(Repo, "msg")).Error.ShouldBe(FlowError.MergeInProgress);
        git.MutatingCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_on_detached_head_is_refused()
    {
        var git = OnFeature(" M a.cs\0");
        git.CurrentBranch = null;

        (await new CommitService(git).CommitAsync(Repo, "msg")).Error.ShouldBe(FlowError.DetachedHead);
    }

    [Fact]
    public async Task Nothing_to_commit_and_empty_message()
    {
        (await new CommitService(OnFeature("")).CommitAsync(Repo, "msg")).Error.ShouldBe(FlowError.NothingToCommit);
        (await new CommitService(OnFeature(" M a.cs\0")).CommitAsync(Repo, "   ")).Error.ShouldBe(FlowError.EmptyCommitMessage);
    }

    [Fact]
    public async Task Failed_commit_is_reported()
    {
        var git = OnFeature(" M a.cs\0");
        git.OnPrefix("commit ", exitCode: 1, stderr: "pre-commit hook failed");

        var result = await new CommitService(git).CommitAsync(Repo, "msg");

        result.Error.ShouldBe(FlowError.GitCommandFailed);
        result.Detail.ShouldBe("pre-commit hook failed");
    }
}
