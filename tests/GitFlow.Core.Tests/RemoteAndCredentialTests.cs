using GitFlow.Core.PullRequests;
using Shouldly;

namespace GitFlow.Core.Tests;

public class RemoteAndCredentialTests
{
    [Theory]
    [InlineData("https://github.com/DNA666777/GitFlow.git")]
    [InlineData("https://github.com/DNA666777/GitFlow")]
    [InlineData("https://someone@github.com/DNA666777/GitFlow.git")]
    [InlineData("git@github.com:DNA666777/GitFlow.git")]
    [InlineData("ssh://git@github.com/DNA666777/GitFlow.git")]
    public void Recognises_github_remotes(string url)
    {
        var remote = RemoteRepository.TryParse(url);

        remote.ShouldNotBeNull();
        remote.Platform.ShouldBe(HostingPlatform.GitHub);
        remote.Owner.ShouldBe("DNA666777");
        remote.Name.ShouldBe("GitFlow");
        remote.CredentialHost.ShouldBe("github.com");
        remote.ApiBase.ShouldBe(new Uri("https://api.github.com/"));
        remote.WebUrl.ShouldBe(new Uri("https://github.com/DNA666777/GitFlow"));
    }

    [Theory]
    [InlineData("https://contoso@dev.azure.com/contoso/My%20Project/_git/My.Repo")]
    [InlineData("https://dev.azure.com/contoso/My%20Project/_git/My.Repo")]
    [InlineData("git@ssh.dev.azure.com:v3/contoso/My%20Project/My.Repo")]
    public void Recognises_azure_devops_remotes(string url)
    {
        var remote = RemoteRepository.TryParse(url);

        remote.ShouldNotBeNull();
        remote.Platform.ShouldBe(HostingPlatform.AzureDevOps);
        remote.Owner.ShouldBe("contoso");
        remote.Project.ShouldBe("My Project");
        remote.Name.ShouldBe("My.Repo");
        remote.CredentialHost.ShouldBe("dev.azure.com");
        remote.CredentialPath.ShouldBe("contoso");
        remote.ApiBase.ShouldBe(new Uri("https://dev.azure.com/contoso/My%20Project/"));
    }

    [Theory]
    [InlineData("https://contoso.visualstudio.com/DefaultCollection/Proj/_git/Repo")]
    [InlineData("https://contoso.visualstudio.com/Proj/_git/Repo")]
    [InlineData("contoso@vs-ssh.visualstudio.com:v3/contoso/Proj/Repo")]
    public void Recognises_legacy_visualstudio_com_remotes(string url)
    {
        var remote = RemoteRepository.TryParse(url);

        remote.ShouldNotBeNull();
        remote.Platform.ShouldBe(HostingPlatform.AzureDevOps);
        remote.Project.ShouldBe("Proj");
        remote.Name.ShouldBe("Repo");
        remote.CredentialHost.ShouldBe("contoso.visualstudio.com");
        remote.ApiBase.ShouldBe(new Uri("https://contoso.visualstudio.com/Proj/"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../GitFlowSandbox-origin.git")]
    [InlineData(@"H:\Dev\repo.git")]
    [InlineData("https://gitlab.com/group/repo.git")]
    [InlineData("https://bitbucket.org/team/repo.git")]
    [InlineData("https://github.com/owner")]
    public void Other_remotes_are_not_recognised(string? url)
    {
        RemoteRepository.TryParse(url).ShouldBeNull();
    }

    [Fact]
    public async Task Credentials_come_from_git_credential_fill()
    {
        var git = new FakeGitRunner().On("credential fill", stdout: "protocol=https\nhost=dev.azure.com\nusername=me\npassword=s3cr3t\n");
        var remote = RemoteRepository.TryParse("https://dev.azure.com/contoso/Proj/_git/Repo")!;

        var credential = await new GitCredentialProvider(git).GetAsync(@"C:\repo", remote);

        credential.ShouldNotBeNull();
        credential.Username.ShouldBe("me");
        credential.Secret.ShouldBe("s3cr3t");
        credential.ToString().ShouldNotContain("s3cr3t");
        git.Inputs.ShouldHaveSingleItem().ShouldBe("protocol=https\nhost=dev.azure.com\npath=contoso\n\n");
        git.Calls.ShouldAllBe(c => !c.Contains("reject") && !c.Contains("erase"));
    }

    [Fact]
    public async Task No_credential_gives_null()
    {
        var git = new FakeGitRunner().On("credential fill", exitCode: 128, stderr: "fatal: could not read Username");
        var remote = RemoteRepository.TryParse("https://github.com/o/r")!;

        (await new GitCredentialProvider(git).GetAsync(@"C:\repo", remote)).ShouldBeNull();
        git.Inputs.ShouldHaveSingleItem().ShouldBe("protocol=https\nhost=github.com\n\n");
    }
}
