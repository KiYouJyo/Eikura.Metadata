using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class PathContextSanitizer
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex NumericRangeDirectoryRegex = new(
        @"^\s*\d{2,4}\s*[-~–—−]\s*\d{2,4}\s*$",
        Options,
        Timeout);

    private static readonly Regex EnglishInstructionDirectoryRegex = new(
        @"^\s*(?:README|HOW\s+TO|INSTRUCTIONS?|SUBTITLES?|SUBS?|FONTS?)\b",
        Options,
        Timeout);

    internal static NormalizedMediaPath Sanitize(NormalizedMediaPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.DirectorySegments.Count == 0)
        {
            return path;
        }

        var raw = new List<string>(path.DirectorySegments.Count);
        var normalized = new List<string>(path.NormalizedDirectorySegments.Count);
        var changed = false;

        for (var i = 0; i < path.DirectorySegments.Count; i++)
        {
            var normalizedValue = path.NormalizedDirectorySegments[i];
            if (IsLowValueDirectory(normalizedValue))
            {
                changed = true;
                continue;
            }

            raw.Add(path.DirectorySegments[i]);
            normalized.Add(normalizedValue);
        }

        return changed
            ? path with
            {
                DirectorySegments = raw,
                NormalizedDirectorySegments = normalized,
            }
            : path;
    }

    internal static bool IsLowValueDirectory(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = TokenClassifier.Normalize(value);
        if (NumericRangeDirectoryRegex.IsMatch(normalized) ||
            EnglishInstructionDirectoryRegex.IsMatch(normalized))
        {
            return true;
        }

        return normalized.Contains("公众号", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("微信公众号", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("如何", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("导入字幕", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("切换音频", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("使用说明", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("播放说明", StringComparison.OrdinalIgnoreCase);
    }
}
