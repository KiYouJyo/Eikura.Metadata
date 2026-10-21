using System.Globalization;
using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class EpisodeBoundaryNormalizer
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex EpisodeYearCollisionRegex = new(
        @"(?:S\d{1,2}E|\d{1,2}x|(?:EPISODE|EP|E)\s*[-_. ]?)(?<episode>\d{1,3})\.(?<year>(?:19|20)\d{2})(?!\d)",
        Options,
        Timeout);

    private static readonly Regex ExtendedSeasonEpisodeRegex = new(
        @"(?<![A-Za-z0-9])S(?<season>\d{1,2})E(?<episode>\d{4})(?!\d)",
        Options,
        Timeout);

    private static readonly Regex LooseBracketEpisodeRegex = new(
        @"^\s*(?:\[(?<group>[^\]]{2,64})\]\s*)?(?<title>.+?)\s*\[(?<episode>\d{1,4})\]\s*(?<tail>(?:\[[^\]]+\]\s*)+)$",
        Options,
        Timeout);

    private static readonly Regex TechnicalTailSignalRegex = new(
        @"(?i)(?:480p|576p|720p|1080p|1440p|2160p|4320p|4k|8k|ma10p|web-?dl|webrip|bluray|bdrip|remux|x264|x265|h264|h265|hevc|avc|av1|aac|flac|ac3|eac3|dts|truehd|10bit|8bit|nvenc|multi[-_ ]?subs?)",
        Options,
        Timeout);

    internal static EpisodeExtractionResult Normalize(
        NormalizedMediaPath path,
        EpisodeExtractionResult result)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(result);

        var corrected = CorrectEpisodeYearCollision(result);
        if (corrected.EpisodeNumber is not null)
        {
            return corrected;
        }

        var extended = ExtendedSeasonEpisodeRegex.Match(path.NormalizedStem);
        if (extended.Success &&
            int.TryParse(extended.Groups["season"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var season) &&
            decimal.TryParse(extended.Groups["episode"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var episode))
        {
            return new EpisodeExtractionResult(
                season,
                episode,
                EpisodeEndNumber: null,
                CourNumber: null,
                Confidence: 0.98,
                new[]
                {
                    new RecognitionEvidence(
                        "episode.sxxexx.extended",
                        extended.Value,
                        0.98),
                });
        }

        if (TryGetLooseBracketEpisode(path, out var bracketEpisode, out _))
        {
            return new EpisodeExtractionResult(
                SeasonNumber: null,
                bracketEpisode,
                EpisodeEndNumber: null,
                CourNumber: null,
                Confidence: 0.90,
                new[]
                {
                    new RecognitionEvidence(
                        "episode.bracket-after-title",
                        bracketEpisode.ToString(CultureInfo.InvariantCulture),
                        0.90),
                });
        }

        return corrected;
    }

    internal static bool TryGetLooseBracketSeriesTitle(
        NormalizedMediaPath path,
        out string title)
    {
        title = string.Empty;
        if (!TryGetLooseBracketEpisode(path, out _, out var parsedTitle))
        {
            return false;
        }

        title = parsedTitle;
        return true;
    }

    private static EpisodeExtractionResult CorrectEpisodeYearCollision(
        EpisodeExtractionResult result)
    {
        if (result.EpisodeNumber is null)
        {
            return result;
        }

        foreach (var evidenceItem in result.Evidence)
        {
            if (evidenceItem.Code is not (
                    "episode.sxxexx" or
                    "episode.onex" or
                    "episode.prefixed") ||
                string.IsNullOrWhiteSpace(evidenceItem.Value))
            {
                continue;
            }

            var match = EpisodeYearCollisionRegex.Match(evidenceItem.Value);
            if (!match.Success ||
                !decimal.TryParse(
                    match.Groups["episode"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var episode))
            {
                continue;
            }

            var evidence = new List<RecognitionEvidence>(result.Evidence)
            {
                new(
                    "episode.year-boundary-correction",
                    match.Groups["year"].Value,
                    0.97),
            };

            return new EpisodeExtractionResult(
                result.SeasonNumber,
                episode,
                result.EpisodeEndNumber,
                result.CourNumber,
                result.Confidence,
                evidence);
        }

        return result;
    }

    private static bool TryGetLooseBracketEpisode(
        NormalizedMediaPath path,
        out decimal episode,
        out string title)
    {
        episode = default;
        title = string.Empty;

        var match = LooseBracketEpisodeRegex.Match(path.Stem);
        if (!match.Success ||
            !decimal.TryParse(
                match.Groups["episode"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out episode) ||
            episode is < 0 or > 9999)
        {
            return false;
        }

        var integerEpisode = decimal.ToInt32(episode);
        if (integerEpisode is 360 or 480 or 576 or 720 or 1080 or 1440 or 2160 or 4320 ||
            integerEpisode is >= 1900 and <= 2099)
        {
            return false;
        }

        var tail = match.Groups["tail"].Value;
        if (TechnicalTailSignalRegex.Matches(tail).Count < 2)
        {
            return false;
        }

        var candidate = match.Groups["title"].Value.Trim(' ', '.', '_', '-', '–', '—', '−');
        if (candidate.Length == 0 ||
            !candidate.Any(static c => char.IsLetter(c) ||
                                      c is >= '\u3040' and <= '\u30ff' ||
                                      c is >= '\u3400' and <= '\u4dbf' ||
                                      c is >= '\u4e00' and <= '\u9fff' ||
                                      c is >= '\uac00' and <= '\ud7af'))
        {
            return false;
        }

        title = candidate;
        return true;
    }
}
