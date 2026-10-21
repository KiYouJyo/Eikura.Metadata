using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class MediaKindResolver
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex SeriesDirectoryRegex = new(
        @"(?:SEASON\s*0?\d{1,2}|(?<![A-Za-z0-9])S\s*0?\d{1,2}(?![A-Za-z0-9])|(?<![A-Za-z])PART\s*0?\d{1,2}(?!\d)|COUR\s*0?\d{1,2}|第\s*0?\d{1,2}\s*(?:季|期|クール|シーズン|シリーズ))",
        Options,
        Timeout);

    internal static MediaKindResolution Resolve(
        NormalizedMediaPath path,
        EpisodeExtractionResult episode,
        DomainClassificationResult domain)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(episode);
        ArgumentNullException.ThrowIfNull(domain);

        if (domain.MediaKind == MediaKind.Movie && IsGenericMovieDirectoryDecision(domain))
        {
            if (HasStrongEpisodeIdentity(episode))
            {
                return new MediaKindResolution(
                    MediaKind.SeriesEpisode,
                    new[]
                    {
                        new RecognitionEvidence(
                            "media-kind.episode-overrides-movie-directory",
                            "strong-episode-identity",
                            0.98),
                    });
            }

            if (HasBareEpisodeIdentity(episode) && HasSeriesDirectoryContext(path))
            {
                return new MediaKindResolution(
                    MediaKind.SeriesEpisode,
                    new[]
                    {
                        new RecognitionEvidence(
                            "media-kind.series-context-overrides-movie-directory",
                            "bare-episode-with-series-directory",
                            0.82),
                    });
            }
        }

        if (domain.MediaKind != MediaKind.Unknown)
        {
            return new MediaKindResolution(domain.MediaKind, Array.Empty<RecognitionEvidence>());
        }

        return episode.EpisodeNumber is null
            ? new MediaKindResolution(MediaKind.Unknown, Array.Empty<RecognitionEvidence>())
            : new MediaKindResolution(MediaKind.SeriesEpisode, Array.Empty<RecognitionEvidence>());
    }

    private static bool IsGenericMovieDirectoryDecision(DomainClassificationResult domain) =>
        domain.Evidence.Any(static item => item.Code == "movie.directory") &&
        !domain.Evidence.Any(static item =>
            item.Code is "movie.japanese-marker" or "movie.english-marker");

    private static bool HasStrongEpisodeIdentity(EpisodeExtractionResult episode) =>
        episode.Evidence.Any(static item =>
            item.Code is
                "episode.sxxexx" or
                "episode.sxxexx.extended" or
                "episode.onex" or
                "episode.prefixed" or
                "episode.japanese-numbered" or
                "episode.bare-delimited" or
                "episode.bracket-sequence" or
                "episode.bracket-after-title" or
                "episode.bare-trailing-release-tag" ||
            item.Code.StartsWith("episode.named-ordinal.", StringComparison.Ordinal));

    private static bool HasBareEpisodeIdentity(EpisodeExtractionResult episode) =>
        episode.Evidence.Any(static item => item.Code == "episode.bare-filename");

    private static bool HasSeriesDirectoryContext(NormalizedMediaPath path) =>
        path.NormalizedDirectorySegments.Any(static directory =>
            SeriesDirectoryRegex.IsMatch(directory));
}

internal sealed record MediaKindResolution(
    MediaKind MediaKind,
    IReadOnlyList<RecognitionEvidence> Evidence);
