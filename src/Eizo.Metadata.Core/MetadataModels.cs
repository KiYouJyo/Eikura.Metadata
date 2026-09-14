using System.Text;
using System.Text.RegularExpressions;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Core;

public enum MetadataSubjectKind
{
    Unknown = 0,
    Series = 1,
    Movie = 2,
}

public enum MetadataEpisodeKind
{
    Regular = 0,
    Special = 1,
    Opening = 2,
    Ending = 3,
    Trailer = 4,
    Other = 5,
}

public enum MetadataContentKind
{
    Unknown = 0,
    Animation = 1,
    LiveAction = 2,
}

public enum MetadataFailureStage
{
    None = 0,
    Search = 1,
    CandidateRanking = 2,
    InstallmentMapping = 3,
    SubjectResolution = 4,
    EpisodeMapping = 5,
    ProviderFetch = 6,
    MetadataMerge = 7,
}

public enum MetadataFailureReason
{
    None = 0,
    MissingSearchTitles = 1,
    NoCandidates = 2,
    ProviderErrorNoCandidates = 3,
    BelowAutoResolveThreshold = 4,
    InsufficientLead = 5,
    StructuralConfirmationRequired = 6,
    ProviderNotRegistered = 7,
    SubjectNotFound = 8,
    SubjectFetchFailed = 9,
    EpisodeNotFound = 10,
    EpisodeFetchFailed = 11,
}

public sealed record MetadataProviderItemId(
    string Provider,
    string Value,
    MetadataSubjectKind Kind);

public sealed record MetadataTitles(
    string Primary,
    string? Original,
    IReadOnlyDictionary<string, string> Localized,
    IReadOnlyList<string> Aliases)
{
    public IEnumerable<string> EnumerateAll()
    {
        if (!string.IsNullOrWhiteSpace(Primary))
        {
            yield return Primary;
        }

        if (!string.IsNullOrWhiteSpace(Original))
        {
            yield return Original;
        }

        foreach (var value in Localized.Values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }

        foreach (var alias in Aliases)
        {
            if (!string.IsNullOrWhiteSpace(alias))
            {
                yield return alias;
            }
        }
    }
}

public sealed record MetadataArtwork(
    string? PosterUrl,
    string? BackdropUrl,
    string? ThumbnailUrl);

public sealed record MetadataSearchRequest(
    IReadOnlyList<string> Titles,
    int? Year,
    MediaKind RecognitionMediaKind,
    int? SeasonNumber,
    decimal? EpisodeNumber,
    string? PreferredLanguage,
    int Limit = 10)
{
    public MetadataSearchRequest ForProviderSearch() =>
        MetadataSearchRequestNormalizer.Normalize(this);

    public static MetadataSearchRequest FromRecognition(
        RecognitionResult recognition,
        string? preferredLanguage = null,
        int limit = 10)
    {
        ArgumentNullException.ThrowIfNull(recognition);

        var titles = new List<string>();

        static void AddTitleAndSearchVariant(List<string> destination, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(value))
            {
                return;
            }

            var title = value.Trim();
            if (!destination.Contains(title, StringComparer.OrdinalIgnoreCase))
            {
                destination.Add(title);
            }

            foreach (var variant in MetadataSearchTitleNormalizer.ExpandSearchVariants(title))
            {
                if (!destination.Contains(variant, StringComparer.OrdinalIgnoreCase))
                {
                    destination.Add(variant);
                }
            }
        }

        AddTitleAndSearchVariant(titles, recognition.Title);

        foreach (var candidate in recognition.TitleCandidates
                     .OrderByDescending(static item => item.IsPrimary)
                     .ThenByDescending(static item => item.Confidence))
        {
            AddTitleAndSearchVariant(titles, candidate.Title);
        }

        // Never turn a low-quality recognition into a broad provider query. If
        // Recognition only produced a weak token, preserve it as a final fallback
        // so the resolver remains debuggable, but do not mix it with strong titles.
        if (titles.Count == 0 && !string.IsNullOrWhiteSpace(recognition.Title))
        {
            titles.Add(recognition.Title.Trim());
        }

        var year = recognition.Year ??
                   MetadataSearchTitleNormalizer.TryExtractTrailingYear(recognition.Title);

        return new MetadataSearchRequest(
            titles.Take(8).ToArray(),
            year,
            recognition.MediaKind,
            recognition.SeasonNumber,
            recognition.EpisodeNumber ?? recognition.SpecialNumber,
            preferredLanguage,
            Math.Clamp(limit, 1, 25));
    }
}

internal static class MetadataSearchTitleNormalizer
{
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex LeadingLibraryOrdinalRegex = new(
        @"^\s*\d{1,2}\s*[._-]?\s*(?:\[(?:19|20)\d{2}\s*[-~–—−]\s*(?:19|20)\d{2}\]\s*)?",
        Options,
        Timeout);

    private static readonly Regex LeadingSeasonTokenRegex = new(
        @"^\s*S(?:EASON)?\s*0?\d{1,2}\s+(?=\p{L}|\p{N}|[\u3040-\u30ff\u3400-\u9fff])",
        Options,
        Timeout);

    private static readonly Regex NamedSeasonSemanticRegex = new(
        @"^\s*(?:(?:S(?:EASON)?\s*0?(?<en>\d{1,2}))|(?<ord>\d{1,2})(?:ST|ND|RD|TH)\s+SEASON|第\s*(?<cn>[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3})\s*(?:季|期))\s*[:：._-]?\s*(?<title>.+?)\s*$",
        Options,
        Timeout);

    private static readonly Regex SeasonCoverageRangeRegex = new(
        @"(?<![\p{L}\p{N}])(?:S(?:EASON)?\s*0?\d{1,2}\s*(?:-|~|～|–|—|−|TO|THROUGH|至|到)\s*(?:S(?:EASON)?\s*)?0?\d{1,2}|第?\s*[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*季\s*(?:-|~|～|–|—|−|TO|THROUGH|至|到)\s*第?\s*[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*季)(?:\s*(?:全|全集|COMPLETE|ALL))?",
        Options,
        Timeout);

    private static readonly Regex CompactSeasonCoverageRangeRegex = new(
        @"(?<![\p{L}\p{N}])(?:第?\s*)?[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*(?:-|~|～|–|—|−|TO|THROUGH|至|到)\s*(?:第?\s*)?[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*(?:季|期)(?:\s*(?:全|全集|COMPLETE|ALL))?",
        Options,
        Timeout);

    private static readonly Regex TrailingYearRegex = new(
        @"(?:[. _-]+|\s*\()(?<year>(?:19\d{2}|20[0-3]\d))\)?\s*$",
        Options,
        Timeout);

    private static readonly Regex TrailingPartRegex = new(
        @"\s*PART\s*0?(?<n>\d{1,2})\s*$",
        Options,
        Timeout);

    private static readonly Regex SpacedSubtitleSeparatorRegex = new(
        @"\s+[-–—−]\s*",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex CjkLatinBilingualRegex = new(
        @"^(?<cjk>.+[\u3040-\u30ff\u3400-\u9fff])\s*[._·|｜]+\s*(?<latin>[A-Z][A-Z0-9 '&+:/-]{2,})$",
        Options,
        Timeout);

    private static readonly Regex ParenthesizedBilingualRegex = new(
        @"^(?<cjk>.+[\u3040-\u30ff\u3400-\u9fff])\s*[\(（]\s*(?<latin>[A-Z][A-Z0-9 '&+:/.-]{2,})\s*[\)）]\s*$",
        Options,
        Timeout);

    private static readonly Regex ProviderIdSuffixRegex = new(
        @"\s*\{\s*(?:tmdb|tmdbid|tvdb|imdb)\s*[-_:]?\s*[^}]+\}\s*$",
        Options,
        Timeout);

    private static readonly Regex WeakStandaloneTitleRegex = new(
        @"^\s*(?:\d{1,2}[. _-]*)?(?:S(?:EASON)?\s*0?\d{1,2}|第\s*[一二三四五六七八九十两兩〇零壹贰貳叁參肆伍陆陸柒捌玖拾\d]{1,3}\s*季|PART\s*\d{1,2}|COUR\s*\d{1,2}|VOL(?:UME)?\s*\d{1,3})\s*$",
        Options,
        Timeout);

    private static readonly Regex BracketOnlyTitleRegex = new(
        @"^\s*(?:\[[^\]]{1,80}\]|【[^】]{1,80}】|\([^)]{1,80}\))\s*$",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex ReleaseGroupOnlyRegex = new(
        @"^\s*[\p{L}\p{N}][\p{L}\p{N} ._&+-]{0,48}(?:STUDIO|RAWS?|SUBS?|字幕(?:组|組|社))\s*$",
        Options,
        Timeout);

    private static readonly Regex MultiSeparatorRegex = new(
        @"[._]+",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex SearchPunctuationRegex = new(
        @"[^\p{L}\p{N}]+",
        RegexOptions.CultureInvariant,
        Timeout);

    private static readonly Regex MultiWhitespaceRegex = new(
        @"\s+",
        RegexOptions.CultureInvariant,
        Timeout);

    internal static int? TryExtractTrailingYear(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        var match = TrailingYearRegex.Match(normalized);
        return match.Success &&
               int.TryParse(match.Groups["year"].Value, out var year)
            ? year
            : null;
    }

    internal static bool TryExtractNamedSeasonSemanticTitle(
        string? value,
        out string semanticTitle)
    {
        semanticTitle = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        if (SeasonCoverageRangeRegex.IsMatch(normalized))
        {
            return false;
        }

        var match = NamedSeasonSemanticRegex.Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        semanticTitle = match.Groups["title"].Value
            .Trim(' ', '-', '–', '—', '−', '_', '.', ':', '：');

        return semanticTitle.Length >= 2 &&
               semanticTitle.Any(static c => char.IsLetterOrDigit(c));
    }

    internal static bool IsSeasonCoverageRange(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        return SeasonCoverageRangeRegex.IsMatch(normalized) ||
               CompactSeasonCoverageRangeRegex.IsMatch(normalized);
    }

    internal static bool IsWeakStandaloneTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC).Trim();
        var withoutCoverage = CompactSeasonCoverageRangeRegex
            .Replace(
                SeasonCoverageRangeRegex.Replace(normalized, string.Empty),
                string.Empty)
            .Trim();
        var hasCoverage =
            SeasonCoverageRangeRegex.IsMatch(normalized) ||
            CompactSeasonCoverageRangeRegex.IsMatch(normalized);

        return normalized.Length < 2 ||
               WeakStandaloneTitleRegex.IsMatch(normalized) ||
               (hasCoverage &&
                withoutCoverage is "" or "全" or "全集") ||
               BracketOnlyTitleRegex.IsMatch(normalized) ||
               ReleaseGroupOnlyRegex.IsMatch(normalized);
    }

    internal static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .Replace('꞉', ':')
            .Trim();

        normalized = ProviderIdSuffixRegex.Replace(normalized, string.Empty);
        normalized = LeadingLibraryOrdinalRegex.Replace(normalized, string.Empty);
        normalized = SeasonCoverageRangeRegex.Replace(normalized, " ");
        normalized = CompactSeasonCoverageRangeRegex.Replace(normalized, " ");

        if (TryExtractNamedSeasonSemanticTitle(normalized, out var namedSeasonTitle))
        {
            normalized = namedSeasonTitle;
        }

        normalized = LeadingSeasonTokenRegex.Replace(normalized, string.Empty);
        normalized = TrailingYearRegex.Replace(normalized, string.Empty);
        normalized = MultiSeparatorRegex.Replace(normalized, " ");
        normalized = MultiWhitespaceRegex.Replace(normalized, " ").Trim();

        return normalized.Trim(' ', '-', '–', '—', '−', '_', '.');
    }

    internal static IReadOnlyList<string> ExpandSearchVariants(string value)
    {
        var variants = new List<string>();

        static void Add(List<string> destination, string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return;
            }

            var trimmed = candidate.Trim(' ', '-', '–', '—', '−', '_', '.');
            if (trimmed.Length < 2 ||
                IsWeakStandaloneTitle(trimmed) ||
                destination.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            destination.Add(trimmed);
        }

        var normalized = Normalize(value);
        Add(variants, normalized);

        var subtitle = SpacedSubtitleSeparatorRegex.Match(normalized);
        if (subtitle.Success && subtitle.Index >= 2)
        {
            Add(variants, normalized[..subtitle.Index]);
        }

        var part = TrailingPartRegex.Match(normalized);
        if (part.Success &&
            int.TryParse(part.Groups["n"].Value, out var partNumber) &&
            partNumber is >= 1 and <= 20)
        {
            var family = normalized[..part.Index]
                .Trim(' ', '-', '–', '—', '−', '_', '.');
            Add(variants, family);
            if (family.Length >= 2)
            {
                Add(variants, $"{family} 第{partNumber}期");
            }
        }

        var sourceNormalized = value
            .Normalize(NormalizationForm.FormKC)
            .Trim();
        sourceNormalized = ProviderIdSuffixRegex.Replace(sourceNormalized, string.Empty);
        sourceNormalized = TrailingYearRegex.Replace(sourceNormalized, string.Empty).Trim();

        var bilingual = CjkLatinBilingualRegex.Match(sourceNormalized);
        if (bilingual.Success)
        {
            Add(variants, Normalize(bilingual.Groups["cjk"].Value));
            Add(variants, Normalize(bilingual.Groups["latin"].Value));
        }

        var parenthesized = ParenthesizedBilingualRegex.Match(sourceNormalized);
        if (parenthesized.Success)
        {
            Add(variants, Normalize(parenthesized.Groups["cjk"].Value));
            Add(variants, Normalize(parenthesized.Groups["latin"].Value));
        }

        var punctuationFolded = SearchPunctuationRegex.Replace(sourceNormalized, " ");
        punctuationFolded = MultiWhitespaceRegex.Replace(punctuationFolded, " ").Trim();
        Add(variants, punctuationFolded);

        return variants;
    }
}

internal static class MetadataSearchRequestNormalizer
{
    internal static MetadataSearchRequest Normalize(MetadataSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var titles = new List<string>();
        string? fallback = null;

        foreach (var value in request.Titles)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var title = value.Trim();
            fallback ??= title;

            if (MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(title))
            {
                continue;
            }

            if (!titles.Contains(title, StringComparer.OrdinalIgnoreCase))
            {
                titles.Add(title);
            }

            foreach (var variant in MetadataSearchTitleNormalizer.ExpandSearchVariants(title))
            {
                if (!titles.Contains(variant, StringComparer.OrdinalIgnoreCase))
                {
                    titles.Add(variant);
                }
            }
        }

        if (titles.Count == 0 && fallback is not null)
        {
            titles.Add(fallback);
        }

        var noisyLibraryTitle =
            titles.Count >= 3 ||
            request.Titles.Any(static title =>
                !string.IsNullOrWhiteSpace(title) &&
                title.Any(static c => char.IsPunctuation(c)));

        if (noisyLibraryTitle && request.Year is >= 1900 and <= 2039)
        {
            var year = request.Year.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var yearBases = titles
                .Select(MetadataSearchTitleNormalizer.Normalize)
                .Where(static title => !string.IsNullOrWhiteSpace(title))
                .Where(static title => !MetadataSearchTitleNormalizer.IsWeakStandaloneTitle(title))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static title => title.Length)
                .Take(3)
                .ToArray();

            foreach (var baseTitle in yearBases)
            {
                var yearPinned = $"{baseTitle} {year}";
                if (!titles.Contains(yearPinned, StringComparer.OrdinalIgnoreCase))
                {
                    titles.Add(yearPinned);
                }
            }
        }

        return request with
        {
            Titles = titles.Take(12).ToArray(),
            Limit = Math.Clamp(request.Limit, 1, 25),
        };
    }
}

public sealed record MetadataSearchCandidate(
    MetadataProviderItemId Id,
    MetadataTitles Titles,
    int? Year,
    int ProviderRank,
    double? Popularity = null)
{
    public MetadataContentKind ContentKind { get; init; } = MetadataContentKind.Unknown;
}

public sealed record MetadataPersonCredit(
    string ProviderPersonId,
    string Name,
    string? Role,
    string? Department,
    string? ProfileUrl,
    int Order);

public sealed record MetadataSubject(
    MetadataProviderItemId Id,
    MetadataTitles Titles,
    string? Overview,
    DateOnly? ReleaseDate,
    int? EpisodeCount,
    MetadataArtwork Artwork,
    IReadOnlyDictionary<string, string> ExternalIds)
{
    public MetadataContentKind ContentKind { get; init; } = MetadataContentKind.Unknown;

    public IReadOnlyList<string> Genres { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ProductionCompanies { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> OriginCountryCodes { get; init; } = Array.Empty<string>();

    public int? RuntimeMinutes { get; init; }

    public string? Status { get; init; }

    public string? OriginalLanguage { get; init; }

    public IReadOnlyList<MetadataPersonCredit> Cast { get; init; } =
        Array.Empty<MetadataPersonCredit>();

    public IReadOnlyList<MetadataPersonCredit> Crew { get; init; } =
        Array.Empty<MetadataPersonCredit>();
}

public sealed record MetadataEpisode(
    string ProviderEpisodeId,
    MetadataProviderItemId SubjectId,
    int? SeasonNumber,
    decimal? EpisodeNumber,
    MetadataEpisodeKind Kind,
    MetadataTitles Titles,
    string? Overview,
    DateOnly? AirDate,
    string? ThumbnailUrl)
{
    public string? ProviderSeasonId { get; init; }

    public string? SeasonTitle { get; init; }

    public string? SeasonOverview { get; init; }

    public DateOnly? SeasonAirDate { get; init; }

    public string? SeasonPosterUrl { get; init; }
}

public sealed record MetadataProviderError(
    string Provider,
    string ErrorType,
    string Message);

public sealed record MetadataResolutionCandidate(
    MetadataSearchCandidate Candidate,
    double Score,
    IReadOnlyList<string> Evidence);

public sealed record MetadataResolution(
    MetadataResolutionCandidate? Best,
    bool IsResolved,
    double Confidence,
    IReadOnlyList<MetadataResolutionCandidate> Candidates,
    IReadOnlyList<MetadataProviderError> ProviderErrors)
{
    public MetadataFailureStage FailureStage { get; init; } = MetadataFailureStage.None;

    public MetadataFailureReason FailureReason { get; init; } = MetadataFailureReason.None;

    public double Lead { get; init; }

    public bool NeedsReview =>
        !IsResolved ||
        FailureStage != MetadataFailureStage.None;
}

public sealed record MetadataEnrichmentResult(
    MetadataResolution Resolution,
    MetadataSubject? Subject,
    MetadataEpisode? Episode,
    IReadOnlyList<MetadataProviderError> ProviderErrors)
{
    public MetadataFailureStage FailureStage { get; init; } = MetadataFailureStage.None;

    public MetadataFailureReason FailureReason { get; init; } = MetadataFailureReason.None;

    public bool NeedsReview =>
        !Resolution.IsResolved ||
        FailureStage != MetadataFailureStage.None;
}

public sealed record MetadataResolverOptions(
    double AutoResolveThreshold = 0.82,
    double MinimumLead = 0.06,
    int MaxCandidates = 20);

public sealed record MetadataCachePolicy(
    TimeSpan SearchTtl,
    TimeSpan SubjectTtl,
    TimeSpan EpisodesTtl)
{
    public static MetadataCachePolicy Default { get; } = new(
        TimeSpan.FromHours(12),
        TimeSpan.FromDays(1),
        TimeSpan.FromHours(6));
}
