using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Eizo.Metadata.Core;

namespace Eizo.Metadata.Providers;

public sealed record AniListArtworkProviderOptions(
    string UserAgent = "KiYouJyo/Eizo",
    string BaseAddress = "https://graphql.anilist.co/");

public sealed class AniListArtworkProvider : IMetadataArtworkProvider
{
    private const string ProviderName = "anilist-artwork";
    private const string Query = """
        query ($search: String, $perPage: Int) {
          Page(page: 1, perPage: $perPage) {
            media(search: $search, type: ANIME, sort: SEARCH_MATCH) {
              id
              idMal
              title {
                romaji
                english
                native
              }
              synonyms
              startDate {
                year
              }
              bannerImage
              coverImage {
                extraLarge
                large
              }
              popularity
            }
          }
        }
        """;

    private readonly HttpClient _httpClient;
    private readonly AniListArtworkProviderOptions _options;

    public AniListArtworkProvider(
        HttpClient httpClient,
        AniListArtworkProviderOptions? options = null)
    {
        _httpClient = httpClient ??
            throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? new AniListArtworkProviderOptions();

        if (!Uri.TryCreate(
                _options.BaseAddress,
                UriKind.Absolute,
                out _))
        {
            throw new ArgumentException(
                "AniList BaseAddress must be an absolute URI.",
                nameof(options));
        }
    }

    public string Name => ProviderName;

    public async Task<MetadataArtwork?> ResolveArtworkAsync(
        MetadataArtworkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ContentKind != MetadataContentKind.Animation)
            return null;

        var titles = request.Titles
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();

        AniListCandidate? best = null;

        foreach (var title in titles)
        {
            var candidate = await SearchBestAsync(
                    title,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);

            if (candidate is null)
                continue;

            if (best is null ||
                candidate.Score > best.Score)
            {
                best = candidate;
            }

            if (candidate.Score >= 0.98)
                break;
        }

        if (best is null || best.Score < 0.62)
            return null;

        return new MetadataArtwork(
            best.PosterUrl,
            best.BannerUrl,
            best.PosterUrl);
    }

    private async Task<AniListCandidate?> SearchBestAsync(
        string search,
        MetadataArtworkRequest request,
        CancellationToken cancellationToken)
    {
        var body = JsonSerializer.Serialize(
            new
            {
                query = Query,
                variables = new
                {
                    search,
                    perPage = 8,
                },
            });

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(
                new Uri(_options.BaseAddress, UriKind.Absolute),
                string.Empty))
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json"),
        };

        message.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        if (!string.IsNullOrWhiteSpace(_options.UserAgent))
        {
            message.Headers.TryAddWithoutValidation(
                "User-Agent",
                _options.UserAgent);
        }

        using var response = await _httpClient
            .SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var document = await JsonDocument.ParseAsync(
                await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!document.RootElement.TryGetProperty(
                "data",
                out var data) ||
            !data.TryGetProperty(
                "Page",
                out var page) ||
            !page.TryGetProperty(
                "media",
                out var media) ||
            media.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return media.EnumerateArray()
            .Select(item => MapCandidate(item, request))
            .Where(static candidate => candidate is not null)
            .Select(static candidate => candidate!)
            .OrderByDescending(static candidate => candidate.Score)
            .ThenByDescending(static candidate => candidate.Popularity)
            .FirstOrDefault();
    }

    private static AniListCandidate? MapCandidate(
        JsonElement item,
        MetadataArtworkRequest request)
    {
        if (!item.TryGetProperty("title", out var titleNode) ||
            titleNode.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var titles = new List<string>();
        AddTitle(titles, titleNode.GetString("romaji"));
        AddTitle(titles, titleNode.GetString("english"));
        AddTitle(titles, titleNode.GetString("native"));

        if (item.TryGetProperty(
                "synonyms",
                out var synonyms) &&
            synonyms.ValueKind == JsonValueKind.Array)
        {
            foreach (var synonym in synonyms.EnumerateArray())
            {
                if (synonym.ValueKind == JsonValueKind.String)
                    AddTitle(titles, synonym.GetString());
            }
        }

        if (titles.Count == 0)
            return null;

        var score = ScoreIdentity(
            request,
            titles,
            item.TryGetProperty("startDate", out var startDate) &&
            startDate.ValueKind == JsonValueKind.Object
                ? startDate.GetInt32("year")
                : null);

        var banner = item.GetString("bannerImage");
        string? poster = null;

        if (item.TryGetProperty(
                "coverImage",
                out var cover) &&
            cover.ValueKind == JsonValueKind.Object)
        {
            poster =
                cover.GetString("extraLarge") ??
                cover.GetString("large");
        }

        if (string.IsNullOrWhiteSpace(banner) &&
            string.IsNullOrWhiteSpace(poster))
        {
            return null;
        }

        return new AniListCandidate(
            score,
            item.GetInt32("popularity") ?? 0,
            banner,
            poster);
    }

    private static double ScoreIdentity(
        MetadataArtworkRequest request,
        IReadOnlyList<string> candidateTitles,
        int? candidateYear)
    {
        var requestTitles = request.Titles
            .Select(NormalizeTitle)
            .Where(static title => title.Length >= 2)
            .ToArray();

        var normalizedCandidates = candidateTitles
            .Select(NormalizeTitle)
            .Where(static title => title.Length >= 2)
            .ToArray();

        var titleScore = 0d;

        foreach (var left in requestTitles)
        {
            foreach (var right in normalizedCandidates)
            {
                if (left == right)
                {
                    titleScore = Math.Max(titleScore, 0.85);
                    continue;
                }

                if (left.Length >= 4 &&
                    right.Length >= 4 &&
                    (left.Contains(
                         right,
                         StringComparison.Ordinal) ||
                     right.Contains(
                         left,
                         StringComparison.Ordinal)))
                {
                    var shorter = Math.Min(
                        left.Length,
                        right.Length);
                    var longer = Math.Max(
                        left.Length,
                        right.Length);

                    titleScore = Math.Max(
                        titleScore,
                        0.62 + 0.18 *
                        ((double)shorter / longer));
                }
            }
        }

        var yearScore = 0d;
        if (request.Year is { } requestedYear &&
            candidateYear is { } actualYear)
        {
            var delta = Math.Abs(
                requestedYear - actualYear);

            yearScore = delta switch
            {
                0 => 0.15,
                1 => 0.07,
                _ => -0.12,
            };
        }

        return Math.Clamp(
            titleScore + yearScore,
            0d,
            1d);
    }

    private static string NormalizeTitle(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();

        var builder = new StringBuilder(
            normalized.Length);

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(character);
        }

        return builder.ToString();
    }

    private static void AddTitle(
        ICollection<string> target,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var title = value.Trim();
        if (!target.Contains(
                title,
                StringComparer.OrdinalIgnoreCase))
        {
            target.Add(title);
        }
    }

    private sealed record AniListCandidate(
        double Score,
        int Popularity,
        string? BannerUrl,
        string? PosterUrl);
}
