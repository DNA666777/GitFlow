using System.Globalization;
using System.Resources;
using GitFlow.Core.Config;
using GitFlow.Core.Model;

namespace GitFlow.Core.Resources;

/// <summary>User facing texts: English by default (Strings.resx), French translation (Strings.fr.resx), following the UI culture.</summary>
public static class FlowMessages
{
    private static readonly ResourceManager Resources = new("GitFlow.Core.Resources.Strings", typeof(FlowMessages).Assembly);

    /// <summary>Culture used for messages; null = current UI culture (falls back to English).</summary>
    public static CultureInfo? Culture { get; set; }

    public static string ReleaseReminder => Get("Info_ReleaseReminder");

    public static string Get(string key) => Resources.GetString(key, Culture) ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(Culture ?? CultureInfo.CurrentUICulture, Get(key), args);

    /// <summary>Message for a failed or conflicting result; empty for a success.</summary>
    public static string Describe(FlowResult result) => result.Status switch
    {
        FlowStatus.Success => string.Empty,
        FlowStatus.Conflict when result.Conflict is { } c =>
            Format("Error_MergeConflict", c.SourceBranch, c.TargetBranch, c.Files.Count),
        _ when result.Error == FlowError.InvalidConfig =>
            Format("Error_InvalidConfig", string.Join(" ", result.ConfigErrors.Select(Describe))),
        _ when result.Error == FlowError.GitCommandFailed =>
            Format("Error_GitCommandFailed", string.IsNullOrWhiteSpace(result.Detail) ? result.FailedCommand?.CommandLine : result.Detail),
        _ => Format("Error_" + result.Error, result.Detail),
    };

    public static string Describe(FlowWarning warning) => Format("Warning_" + warning.Kind, warning.Detail);

    public static string Describe(ConfigValidationError error) => Format("Config_" + error.Kind, error.Value);

    public static string Describe(SettingsWarning warning)
    {
        const int MaxListed = 5;
        var list = string.Join(", ", warning.Items.Take(MaxListed)) + (warning.Items.Count > MaxListed ? ", …" : string.Empty);
        var prefix = warning.Prefix.Length == 0 ? "∅" : warning.Prefix;
        return Format("SettingsWarning_" + warning.Kind, prefix, warning.Items.Count, list);
    }
}
