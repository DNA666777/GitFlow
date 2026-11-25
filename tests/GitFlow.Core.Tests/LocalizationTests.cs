using System.Collections;
using System.Globalization;
using System.Resources;
using GitFlow.Core.Model;
using GitFlow.Core.PullRequests;
using GitFlow.Core.Resources;
using Shouldly;

namespace GitFlow.Core.Tests;

/// <summary>
/// <see cref="FlowMessages.Culture"/> is process-wide: every test class that sets it belongs to this collection,
/// so that they never run in parallel with each other.
/// </summary>
[CollectionDefinition(Collection)]
public sealed class MessagesCulture
{
    public const string Collection = "FlowMessages culture";

    public static CultureInfo French { get; } = new("fr-FR");

    public static CultureInfo English { get; } = new("en-US");
}

[Collection(MessagesCulture.Collection)]
public class LocalizationTests
{
    private static readonly ResourceManager Resources = new("GitFlow.Core.Resources.Strings", typeof(FlowMessages).Assembly);

    private static string Nl => Environment.NewLine;

    [Fact]
    public void English_is_the_default_language()
    {
        FlowMessages.Culture = CultureInfo.InvariantCulture;

        FlowMessages.Describe(FlowResult.Fail(FlowError.BranchAlreadyExists, "feature/x")).ShouldBe("Branch \"feature/x\" already exists.");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("ja-JP")]
    public void Languages_without_translation_fall_back_to_english(string culture)
    {
        FlowMessages.Culture = new CultureInfo(culture);

        FlowMessages.ReleaseReminder.ShouldBe("Release in progress: bug fixes, docs and release tasks only.");
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("fr-BE")]
    [InlineData("fr-CA")]
    [InlineData("fr")]
    public void Every_french_culture_gets_the_french_translation(string culture)
    {
        FlowMessages.Culture = new CultureInfo(culture);

        FlowMessages.ReleaseReminder.ShouldBe("Release en cours : corrections, docs et tâches de release uniquement.");
    }

    [Fact]
    public void French_translation_has_every_key_of_the_english_resources()
    {
        // tryParents: false = only the French file, without falling back to English for a missing key.
        var english = Keys(Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false));
        var french = Keys(Resources.GetResourceSet(new CultureInfo("fr"), createIfNotExists: true, tryParents: false));

        english.ShouldNotBeEmpty();
        english.Except(french).ShouldBeEmpty("keys missing in Strings.fr.resx");
        french.Except(english).ShouldBeEmpty("keys missing in Strings.resx");
    }

    [Fact]
    public void Every_error_warning_and_config_message_exists_in_english()
    {
        FlowMessages.Culture = MessagesCulture.English;

        foreach (var error in Enum.GetValues<FlowError>())
        {
            FlowMessages.Get("Error_" + error).ShouldNotBe("Error_" + error);
        }

        foreach (var kind in Enum.GetValues<FlowWarningKind>())
        {
            FlowMessages.Get("Warning_" + kind).ShouldNotBe("Warning_" + kind);
        }

        foreach (var kind in Enum.GetValues<Config.ConfigValidationErrorKind>())
        {
            FlowMessages.Get("Config_" + kind).ShouldNotBe("Config_" + kind);
        }
    }

    [Fact]
    public void English_conflict_message()
    {
        FlowMessages.Culture = MessagesCulture.English;

        FlowMessages.Describe(FlowResult.Conflicted(new MergeConflict("feature/x", "develop", ["a", "b"])))
            .ShouldStartWith("Conflicts while merging \"feature/x\" into \"develop\" (2 file(s)).");
    }

    [Fact]
    public void English_commit_message()
    {
        FlowMessages.Culture = MessagesCulture.English;

        var message = Flow.CommitMessageGenerator.Generate(
            [
                new FileChange(ChangeKind.Added, "src/App/Login.cs", null, false, true),
                new FileChange(ChangeKind.Modified, "src/App/Program.cs", null, false, true),
            ],
            new Dictionary<string, FileStat> { ["src/App/Program.cs"] = new(12, 3) },
            "feature/ABC-12-login");

        message.ShouldBe(string.Join(Nl,
            "Update 2 files in src/App",
            "",
            "Added (1):",
            "- src/App/Login.cs",
            "Modified (1):",
            "- src/App/Program.cs (+12 -3)",
            "",
            "Refs: ABC-12"));
    }

    [Fact]
    public void English_single_file_subject()
    {
        FlowMessages.Culture = MessagesCulture.English;

        Flow.CommitMessageGenerator.Generate([new FileChange(ChangeKind.Renamed, "B.cs", "A.cs", true, false)], new Dictionary<string, FileStat>(), null)
            .Split(Nl)[0].ShouldBe("Rename A.cs to B.cs");
    }

    [Fact]
    public void English_pull_request_description()
    {
        FlowMessages.Culture = MessagesCulture.English;
        var context = new PullRequestContext(new FlowBranch(BranchType.Release, "1.7.0", "release/1.7.0"), "master", [], "", new DiffSummary(3, 10, 2), "1.7.0");

        var draft = PullRequestMessageGenerator.Build(context);

        draft.Title.ShouldBe("Release 1.7.0");
        draft.Description.ShouldBe(string.Join(Nl,
            "**Release** `release/1.7.0` → `master`",
            "",
            "### Commits (0)",
            "_No commit._",
            "",
            "### Files",
            "3 file(s) changed, 10 line(s) added, 2 line(s) deleted",
            "",
            "### Version",
            "Version **1.7.0**: tag `1.7.0` will be created on the production branch once this PR is merged."));
    }

    private static HashSet<string> Keys(ResourceSet? set) =>
        set is null ? [] : set.Cast<DictionaryEntry>().Select(e => (string)e.Key).ToHashSet(StringComparer.Ordinal);
}
