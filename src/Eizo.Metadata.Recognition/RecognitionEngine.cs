using Eizo.Metadata.Recognition.Internal;

namespace Eizo.Metadata.Recognition;

/// <summary>
/// Default deterministic, provider-neutral recognition engine.
/// </summary>
public sealed class RecognitionEngine : IRecognitionEngine
{
    public RecognitionResult Recognize(RecognitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var path = PathContextSanitizer.Sanitize(
            PathPreprocessor.Preprocess(request.Path));
        var technicalSuffix = TechnicalSuffixAnalyzer.Analyze(path);
        var episode = EpisodeBoundaryNormalizer.Normalize(
            path,
            EpisodeExtractor.Extract(path));
        var episodeTitle = EpisodeTitleBoundaryResolver.Normalize(
            path,
            episode,
            EpisodeTitleExtractor.Extract(path, episode));
        var domain = DomainClassifier.Classify(path);
        var title = TitleBoundaryNormalizer.Normalize(
            path,
            episode,
            TitleExtractor.Extract(path, episode, domain, episodeTitle));

        var evidence = new List<RecognitionEvidence>(episode.Evidence);
        if (technicalSuffix is not null)
        {
            evidence.AddRange(technicalSuffix.Evidence);
        }

        evidence.AddRange(episodeTitle.Evidence);
        evidence.AddRange(domain.Evidence);
        evidence.AddRange(title.Evidence);

        var year = TryGetYear(path, technicalSuffix, evidence);
        var mediaKindResolution = MediaKindResolver.Resolve(path, episode, domain);
        evidence.AddRange(mediaKindResolution.Evidence);
        var mediaKind = mediaKindResolution.MediaKind;

        var episodeNumber = episode.EpisodeNumber;
        var episodeEndNumber = episode.EpisodeEndNumber;
        var specialNumber = domain.SpecialNumber;

        if (mediaKind == MediaKind.Special)
        {
            if (specialNumber is null &&
                episodeNumber is not null &&
                episode.Evidence.Any(static item => item.Code == "episode.bare-filename"))
            {
                specialNumber = episodeNumber;
                evidence.Add(new RecognitionEvidence(
                    "special.number.from-bare-filename",
                    episodeNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    0.70));
            }

            episodeNumber = null;
            episodeEndNumber = null;
        }
        else if (mediaKind == MediaKind.Movie)
        {
            episodeNumber = null;
            episodeEndNumber = null;
        }

        var confidence = ConfidenceScorer.Assess(
            mediaKind,
            episode,
            title,
            domain);

        evidence.AddRange(confidence.Evidence);

        return new RecognitionResult(
            mediaKind,
            domain.SpecialKind,
            domain.EpisodePart,
            domain.IsFinalEpisode,
            title.Title,
            ToPublicTitleCandidates(title),
            episode.SeasonNumber,
            episode.CourNumber,
            episodeNumber,
            episodeEndNumber,
            specialNumber,
            year,
            confidence.Score,
            confidence.Level,
            confidence.IsAmbiguous,
            evidence)
        {
            EpisodeTitle = episodeTitle.EpisodeTitle,
        };
    }

    private static IReadOnlyList<RecognitionTitleCandidate> ToPublicTitleCandidates(
        TitleExtractionResult title)
    {
        if (title.Candidates.Count == 0)
        {
            return Array.Empty<RecognitionTitleCandidate>();
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<RecognitionTitleCandidate>();

        foreach (var candidate in title.Candidates)
        {
            if (!seen.Add(candidate.Title))
            {
                continue;
            }

            result.Add(new RecognitionTitleCandidate(
                candidate.Title,
                candidate.Confidence,
                candidate.Source,
                string.Equals(candidate.Title, title.Title, StringComparison.Ordinal)));
        }

        return result;
    }

    private static int? TryGetYear(
        NormalizedMediaPath path,
        TechnicalSuffixResult? technicalSuffix,
        ICollection<RecognitionEvidence> evidence)
    {
        var token = technicalSuffix?.YearToken ??
                    path.Tokens.FirstOrDefault(static token => token.Kind == TokenKind.Year);
        if (token is null || !int.TryParse(token.NormalizedValue, out var year))
        {
            return null;
        }

        evidence.Add(new RecognitionEvidence("year.token", token.NormalizedValue, 0.80));
        return year;
    }
}
