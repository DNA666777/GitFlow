using GitFlow.Core.Model;

namespace GitFlow.Server.Tests;

/// <summary>The Commit section of the GitFlow window through JSON-RPC.</summary>
public class ServerCommitTests
{
    [Fact]
    public async Task Preview_lists_the_changes_and_generates_the_message()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "ABC-12-login" });
        await s.Repo.WriteFileAsync("login.txt", "login\n");
        await s.Repo.WriteFileAsync("README.md", "changed\n");

        var preview = await s.CallAsync<CommitPreviewDto?>("getCommitPreview", new { repository = s.Repo.WorkDir });

        preview!.Mode.ShouldBe(CommitMode.AllChanges);
        preview.Changes.Select(c => (c.Kind, c.Path)).ShouldBe([(ChangeKind.Modified, "README.md"), (ChangeKind.Added, "login.txt")], ignoreOrder: true);
        preview.SuggestedMessage.ShouldStartWith("Met à jour 2 fichiers");
        preview.SuggestedMessage.ShouldEndWith("Réf. : ABC-12");
        preview.IsBlocked.ShouldBeFalse();
        preview.BlockedMessage.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_with_the_message_of_the_user()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.CallAsync<ResultDto>("start", new { repository = s.Repo.WorkDir, type = "feature", name = "x" });
        await s.Repo.WriteFileAsync("x.txt", "x\n");

        var result = await s.CallAsync<ResultDto>("commit", new { repository = s.Repo.WorkDir, message = "Ajoute x" });
        var after = await s.CallAsync<CommitPreviewDto?>("getCommitPreview", new { repository = s.Repo.WorkDir });

        result.Success.ShouldBeTrue(result.Message);
        (await s.Repo.GitAsync("log", "-1", "--format=%s")).ShouldBe("Ajoute x");
        after!.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Commit_on_master_is_refused_with_the_reason()
    {
        await using var s = await ServerSession.CreateAsync();
        await s.Repo.GitAsync("switch", "master");
        await s.Repo.WriteFileAsync("README.md", "changed\n");

        var preview = await s.CallAsync<CommitPreviewDto?>("getCommitPreview", new { repository = s.Repo.WorkDir });
        var result = await s.CallAsync<ResultDto>("commit", new { repository = s.Repo.WorkDir, message = "Direct" });

        preview!.IsBlocked.ShouldBeTrue();
        preview.BlockedMessage.ShouldBe("Pas de commit direct sur « master » : démarrez une feature (ou un hotfix pour la production).");
        result.Error.ShouldBe(FlowError.CommitOnMasterRefused);
    }
}
