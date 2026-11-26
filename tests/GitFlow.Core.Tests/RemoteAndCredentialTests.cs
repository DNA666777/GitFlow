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
    [InlineData("https://dev.azure.com/contoso/My%20Project/_git/My.Repo/")]
    [InlineData("https://dev.azure.com/contoso/My%20Project/_git/My.Repo.git")]
    [InlineData("https://DEV.AZURE.COM/contoso/My%20Project/_git/My.Repo")]
    [InlineData("https://contoso@dev.azure.com:443/contoso/My%20Project/_git/My.Repo")]
    [InlineData("https://dev.azure.com/contoso/DefaultCollection/My%20Project/_git/My.Repo")]
    [InlineData("https://dev.azure.com/contoso/My%20Project/_git/_optimized/My.Repo")]
    [InlineData("ssh://git@ssh.dev.azure.com/v3/contoso/My%20Project/My.Repo")]
    [InlineData("ssh://git@ssh.dev.azure.com:22/v3/contoso/My%20Project/My.Repo")]
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
    [InlineData("https://Contoso.VisualStudio.com/Proj/_git/Repo")]
    [InlineData("https://user@contoso.visualstudio.com/DefaultCollection/Proj/_git/Repo")]
    [InlineData("ssh://contoso@vs-ssh.visualstudio.com:22/v3/contoso/Proj/Repo")]
    [InlineData("ssh://contoso@vs-ssh.visualstudio.com:22/DefaultCollection/Proj/_ssh/Repo")]
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
    [InlineData("https://dev.azure.com/contoso/_git/Repo")]
    [InlineData("https://contoso@dev.azure.com/contoso/_git/Repo")]
    [InlineData("https://contoso.visualstudio.com/_git/Repo")]
    [InlineData("https://contoso.visualstudio.com/DefaultCollection/_git/Repo")]
    public void Azure_devops_remote_without_project_uses_the_repository_name(string url)
    {
        var remote = RemoteRepository.TryParse(url);

        remote.ShouldNotBeNull();
        remote.Platform.ShouldBe(HostingPlatform.AzureDevOps);
        remote.Owner.ShouldBe("contoso");
        remote.Project.ShouldBe("Repo");
        remote.Name.ShouldBe("Repo");
    }

    [Theory]
    [InlineData("ssh://github.com/DNA666777/GitFlow.git")]
    [InlineData("ssh://git@ssh.github.com:443/DNA666777/GitFlow.git")]
    [InlineData("https://www.github.com/DNA666777/GitFlow/")]
    public void Recognises_other_github_forms(string url)
    {
        var remote = RemoteRepository.TryParse(url);

        remote.ShouldNotBeNull();
        remote.Owner.ShouldBe("DNA666777");
        remote.Name.ShouldBe("GitFlow");
    }

    [Theory]
    [InlineData("https://tfs.contoso.com/tfs/Collection/My%20Project/_git/My.Repo", "https://tfs.contoso.com/tfs/Collection/My%20Project/", "tfs.contoso.com", "https")]
    [InlineData("https://someone@tfs.contoso.com:8443/tfs/Collection/My%20Project/_git/My.Repo/", "https://tfs.contoso.com:8443/tfs/Collection/My%20Project/", "tfs.contoso.com:8443", "https")]
    [InlineData("http://devops:8080/tfs/DefaultCollection/My%20Project/_git/My.Repo", "http://devops:8080/tfs/DefaultCollection/My%20Project/", "devops:8080", "http")]
    [InlineData("https://git.contoso.com/My%20Project/_git/_optimized/My.Repo", "https://git.contoso.com/My%20Project/", "git.contoso.com", "https")]
    [InlineData("ssh://devops.contoso.com:22/tfs/Collection/My%20Project/_git/My.Repo", "https://devops.contoso.com/tfs/Collection/My%20Project/", "devops.contoso.com", "https")]
    [InlineData("git@devops.contoso.com:tfs/Collection/My%20Project/_git/My.Repo", "https://devops.contoso.com/tfs/Collection/My%20Project/", "devops.contoso.com", "https")]
    public void Declared_azure_devops_server_accepts_any_git_url(string url, string apiBase, string credentialHost, string protocol)
    {
        RemoteRepository.TryParse(url).ShouldBeNull();

        var remote = RemoteRepository.TryParse(url, azureDevOpsServer: true);

        remote.ShouldNotBeNull();
        remote.Platform.ShouldBe(HostingPlatform.AzureDevOps);
        remote.OnPremises.ShouldBeTrue();
        remote.Project.ShouldBe("My Project");
        remote.Name.ShouldBe("My.Repo");
        remote.ApiBase.ShouldBe(new Uri(apiBase));
        remote.WebUrl.ShouldBe(new Uri(apiBase + "_git/My.Repo"));
        remote.CredentialHost.ShouldBe(credentialHost);
        remote.CredentialProtocol.ShouldBe(protocol);
        remote.CredentialPath.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://tfs.contoso.com/tfs/Collection/Proj/Repo")]
    [InlineData("https://tfs.contoso.com/_git/Repo")]
    [InlineData("../origin.git")]
    public void Declared_azure_devops_server_still_needs_a_git_url(string url)
    {
        RemoteRepository.TryParse(url, azureDevOpsServer: true).ShouldBeNull();
    }

    [Fact]
    public void Declaring_a_server_does_not_change_recognised_remotes()
    {
        var remote = RemoteRepository.TryParse("https://dev.azure.com/contoso/Proj/_git/Repo", azureDevOpsServer: true)!;

        remote.OnPremises.ShouldBeFalse();
        remote.ApiBase.ShouldBe(new Uri("https://dev.azure.com/contoso/Proj/"));
        RemoteRepository.TryParse("https://github.com/o/r", azureDevOpsServer: true)!.Platform.ShouldBe(HostingPlatform.GitHub);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("auto", false)]
    [InlineData("azuredevops", true)]
    public async Task Origin_is_an_azure_devops_server_only_when_declared(string? platform, bool recognised)
    {
        var git = new FakeGitRunner().On("config --get remote.origin.url", stdout: "https://tfs.contoso.com/tfs/Coll/Proj/_git/Repo\n");
        if (platform is not null)
        {
            git.On("config --get gitflowvs.pr.platform", stdout: platform + "\n");
        }
        else
        {
            git.On("config --get gitflowvs.pr.platform", exitCode: 1);
        }

        var remote = await new PullRequestService(git, new HttpClient(new FakeHttpHandler())).GetRemoteAsync("C:/repo");

        (remote is not null).ShouldBe(recognised);
        remote?.OnPremises.ShouldBeTrue();
    }

    [Fact]
    public async Task Server_credentials_are_asked_for_its_host_and_protocol()
    {
        var git = new FakeGitRunner().On("credential fill", stdout: "protocol=http\nhost=devops:8080\nusername=me\npassword=s3cr3t\n");
        var remote = RemoteRepository.TryParse("http://devops:8080/tfs/Coll/Proj/_git/Repo", azureDevOpsServer: true)!;

        var credential = await new GitCredentialProvider(git).GetAsync("C:/repo", remote);

        credential!.Secret.ShouldBe("s3cr3t");
        git.Inputs.ShouldHaveSingleItem().ShouldBe("protocol=http\nhost=devops:8080\n\n");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("C:/Dev/repo.git")]
    [InlineData("file:///H:/Dev/repo.git")]
    [InlineData("https://dev.azure.com/contoso")]
    [InlineData("https://dev.azure.com/contoso/Proj/_git")]
    [InlineData("https://tfs.contoso.com/tfs/Collection/Proj/_git/Repo")]
    [InlineData("https://evil.com/contoso.visualstudio.com/Proj/_git/Repo")]
    [InlineData("https://a.b.visualstudio.com/Proj/_git/Repo")]
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
