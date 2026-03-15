using System.Text.RegularExpressions;

namespace AzureWebLogDownloader.Services;

public sealed class BlobPathTemplateResolver
{
    private static readonly IReadOnlyDictionary<string, string> TokenPatterns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["instance"] = @"(?<instance>[^/]+)",
        ["yyyy"] = @"(?<yyyy>\d{4})",
        ["MM"] = @"(?<MM>\d{2})",
        ["dd"] = @"(?<dd>\d{2})",
        ["HH"] = @"(?<HH>\d{2})",
        ["filename"] = @"(?<filename>[^/]+)"
    };

    public bool TryResolve(
        string blobPath,
        IReadOnlyList<string> orderedTemplates,
        out BlobPathMatch? match,
        Action<string>? traceLogger = null)
    {
        match = null;
        if (string.IsNullOrWhiteSpace(blobPath) || orderedTemplates.Count == 0)
        {
            return false;
        }

        var normalizedBlobPath = NormalizePath(blobPath);
        for (var index = 0; index < orderedTemplates.Count; index++)
        {
            var template = orderedTemplates[index];
            if (string.IsNullOrWhiteSpace(template))
            {
                continue;
            }

            if (!TryMatchTemplate(normalizedBlobPath, template, index, out var templateMatch))
            {
                continue;
            }

            match = templateMatch;
            traceLogger?.Invoke(
                $"Blob path '{normalizedBlobPath}' matched template[{index}] '{templateMatch.Template}' " +
                $"for instance '{templateMatch.Instance}' at '{templateMatch.TimestampUtc:yyyy-MM-dd HH:mm} UTC'.");
            return true;
        }

        return false;
    }

    public IReadOnlyList<BlobPathMatch> ResolveDeterministicConflicts(IEnumerable<BlobPathMatch> matches)
    {
        var resolved = matches
            .GroupBy(match => match.LogicalMinuteKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(match => match.TemplateOrder)
                .ThenBy(match => match.BlobPath, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(match => match.LogicalMinuteKey, StringComparer.Ordinal)
            .ToList();

        return resolved;
    }

    private static bool TryMatchTemplate(string blobPath, string template, int templateOrder, out BlobPathMatch match)
    {
        match = default!;
        var regexPattern = BuildRegexPattern(template);
        var regex = new Regex(
            regexPattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var regexMatch = regex.Match(blobPath);
        if (!regexMatch.Success)
        {
            return false;
        }

        var instance = ValueOrEmpty(regexMatch.Groups["instance"].Value);
        var filename = ValueOrEmpty(regexMatch.Groups["filename"].Value);
        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        if (!TryBuildTimestamp(regexMatch, out var timestampUtc))
        {
            return false;
        }

        var minuteUtc = new DateTime(
            timestampUtc.Year,
            timestampUtc.Month,
            timestampUtc.Day,
            timestampUtc.Hour,
            0,
            0,
            DateTimeKind.Utc);

        var logicalMinuteKey = $"{instance}|{minuteUtc:yyyyMMddHHmm}";

        match = new BlobPathMatch(
            blobPath,
            template,
            templateOrder,
            instance,
            filename,
            timestampUtc,
            logicalMinuteKey);
        return true;
    }

    private static string BuildRegexPattern(string template)
    {
        var normalized = NormalizePath(template);
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal);
        var placeholderIndex = 0;
        foreach (var token in TokenPatterns)
        {
            var placeholder = $"__TOKEN_{placeholderIndex}__";
            normalized = normalized.Replace("{" + token.Key + "}", placeholder, StringComparison.Ordinal);
            placeholders[placeholder] = token.Value;
            placeholderIndex++;
        }

        var escaped = Regex.Escape(normalized);
        foreach (var placeholder in placeholders)
        {
            escaped = escaped.Replace(placeholder.Key, placeholder.Value, StringComparison.Ordinal);
        }

        return $"^{escaped}$";
    }

    private static bool TryBuildTimestamp(Match regexMatch, out DateTime timestampUtc)
    {
        timestampUtc = default;

        if (!int.TryParse(regexMatch.Groups["yyyy"].Value, out var year) ||
            !int.TryParse(regexMatch.Groups["MM"].Value, out var month) ||
            !int.TryParse(regexMatch.Groups["dd"].Value, out var day))
        {
            return false;
        }

        var hour = 0;
        _ = int.TryParse(regexMatch.Groups["HH"].Value, out hour);

        try
        {
            timestampUtc = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string NormalizePath(string value) =>
        value.Replace('\\', '/').Trim('/');

    private static string ValueOrEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value;
}

public sealed record BlobPathMatch(
    string BlobPath,
    string Template,
    int TemplateOrder,
    string Instance,
    string FileName,
    DateTime TimestampUtc,
    string LogicalMinuteKey);
