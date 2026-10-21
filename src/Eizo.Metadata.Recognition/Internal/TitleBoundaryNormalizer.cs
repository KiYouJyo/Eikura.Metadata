using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class TitleBoundaryNormalizer
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex ExtendedSeasonEpisodeTitleRegex = new(
        @"^(?<title>.+?)(?:[\s._-]+(?:19|20)\d{2})?[\s._-]*S\d{1,2}E\d{4}(?!\d)",
        Options,
        Timeout);

    internal static TitleExtractionResult Normalize(
        NormalizedMediaPath path,
        EpisodeExtractionResult episode,
        TitleExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(result);

        if (episode.Evidence.Any(static item => item.Code == "episode.bracket-after-title") &&
            EpisodeBoundaryNormalizer.TryGetLooseBracketSeriesTitle(path, out var bracketTitle))
        {
            return ReplacePrimary(result, bracketTitle, "bracket-episode", 0.92);
        }

        if (episode.Evidence.Any(static item => item.Code == "episode.sxxexx.extended"))
        {
            var match = ExtendedSeasonEpisodeTitleRegex.Match(path.Stem);
            if (match.Success)
            {
                var title = match.Groups["title"].Value.Trim(' ', '.', '_', '-', '–', '—', '−');
                if (IsUseful(title))
                {
                    return ReplacePrimary(result, title, "filename-extended-sxxexx", 0.92);
                }
            }
        }

        return result;
    }

    private static TitleExtractionResult ReplacePrimary(
        TitleExtractionResult result,
        string title,
        string source,
        double confidence)
    {
        var candidates = new List<TitleCandidate>
        {
            new(title, source, confidence, SourcePriority: 0),
        };

        candidates.AddRange(result.Candidates.Where(candidate =>
            candidate.Source == "parent-directory" &&
            !string.Equals(candidate.Title, title, StringComparison.Ordinal)));

        var evidence = new List<RecognitionEvidence>(result.Evidence)
        {
            new($"title.{source}", title, confidence),
        };

        return new TitleExtractionResult(
            title,
            confidence,
            candidates,
            evidence);
    }

    private static bool IsUseful(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Any(static c => char.IsLetter(c) ||
                              c is >= '\u3040' and <= '\u30ff' ||
                              c is >= '\u3400' and <= '\u4dbf' ||
                              c is >= '\u4e00' and <= '\u9fff' ||
                              c is >= '\uac00' and <= '\ud7af');
}
