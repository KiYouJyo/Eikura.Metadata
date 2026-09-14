using System.Text.Json;

namespace Eizo.Metadata.Core;

public sealed record MetadataArtworkRequest(
    IReadOnlyList<string> Titles,
    int? Year,
    MetadataSubjectKind SubjectKind,
    MetadataContentKind ContentKind,
    string? PreferredLanguage,
    IReadOnlyDictionary<string, string> ExternalIds)
{
    public MetadataArtworkRequest Normalize()
    {
        var titles = Titles
            .Where(static title => !string.IsNullOrWhiteSpace(title))
            .Select(static title => title.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return this with
        {
            Titles = titles,
            ExternalIds = new Dictionary<string, string>(
                ExternalIds,
                StringComparer.OrdinalIgnoreCase),
        };
    }
}

public sealed record MetadataArtworkResolution(
    MetadataArtwork Artwork,
    IReadOnlyList<string> Providers,
    IReadOnlyList<MetadataProviderError> ProviderErrors)
{
    public bool HasArtwork =>
        !string.IsNullOrWhiteSpace(Artwork.PosterUrl) ||
        !string.IsNullOrWhiteSpace(Artwork.BackdropUrl) ||
        !string.IsNullOrWhiteSpace(Artwork.ThumbnailUrl);
}

public interface IMetadataArtworkProvider
{
    string Name { get; }

    Task<MetadataArtwork?> ResolveArtworkAsync(
        MetadataArtworkRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CachedMetadataArtworkProvider :
    IMetadataArtworkProvider
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IMetadataArtworkProvider _inner;
    private readonly IMetadataCache _cache;
    private readonly TimeSpan _ttl;

    public CachedMetadataArtworkProvider(
        IMetadataArtworkProvider inner,
        IMetadataCache cache,
        TimeSpan? ttl = null)
    {
        _inner = inner ??
            throw new ArgumentNullException(nameof(inner));
        _cache = cache ??
            throw new ArgumentNullException(nameof(cache));
        _ttl = ttl ?? TimeSpan.FromDays(14);
    }

    public string Name => _inner.Name;

    public async Task<MetadataArtwork?> ResolveArtworkAsync(
        MetadataArtworkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request = request.Normalize();
        var key = BuildKey(request);

        var payload = await _cache
            .GetAsync(key, cancellationToken)
            .ConfigureAwait(false);

        if (payload is not null)
        {
            try
            {
                return JsonSerializer.Deserialize<MetadataArtwork>(
                    payload,
                    JsonOptions);
            }
            catch (JsonException)
            {
            }
        }

        var result = await _inner
            .ResolveArtworkAsync(
                request,
                cancellationToken)
            .ConfigureAwait(false);

        if (result is not null)
        {
            await _cache
                .SetAsync(
                    key,
                    JsonSerializer.Serialize(
                        result,
                        JsonOptions),
                    DateTimeOffset.UtcNow.Add(_ttl),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return result;
    }

    private string BuildKey(
        MetadataArtworkRequest request)
    {
        var titles = string.Join(
            "\u001f",
            request.Titles);

        var externalIds = string.Join(
            "\u001f",
            request.ExternalIds
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => $"{pair.Key}={pair.Value}"));

        return $"artwork|{Name}|{request.SubjectKind}|{request.ContentKind}|{request.Year}|{request.PreferredLanguage}|{titles}|{externalIds}";
    }
}

public sealed class MetadataArtworkResolver
{
    private readonly IReadOnlyList<IMetadataArtworkProvider> _providers;

    public MetadataArtworkResolver(
        IEnumerable<IMetadataArtworkProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers
            .Where(static provider => provider is not null)
            .ToArray();
    }

    public async Task<MetadataArtworkResolution> ResolveAsync(
        MetadataArtworkRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        request = request.Normalize();
        if (request.Titles.Count == 0 || _providers.Count == 0)
        {
            return new MetadataArtworkResolution(
                new MetadataArtwork(null, null, null),
                [],
                []);
        }

        string? poster = null;
        string? backdrop = null;
        string? thumbnail = null;
        var providers = new List<string>();
        var errors = new List<MetadataProviderError>();

        foreach (var provider in _providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var artwork = await provider
                    .ResolveArtworkAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                if (artwork is null)
                    continue;

                var contributed = false;

                if (string.IsNullOrWhiteSpace(backdrop) &&
                    !string.IsNullOrWhiteSpace(artwork.BackdropUrl))
                {
                    backdrop = artwork.BackdropUrl;
                    contributed = true;
                }

                if (string.IsNullOrWhiteSpace(poster) &&
                    !string.IsNullOrWhiteSpace(artwork.PosterUrl))
                {
                    poster = artwork.PosterUrl;
                    contributed = true;
                }

                if (string.IsNullOrWhiteSpace(thumbnail) &&
                    !string.IsNullOrWhiteSpace(artwork.ThumbnailUrl))
                {
                    thumbnail = artwork.ThumbnailUrl;
                    contributed = true;
                }

                if (contributed)
                    providers.Add(provider.Name);

                if (!string.IsNullOrWhiteSpace(backdrop) &&
                    !string.IsNullOrWhiteSpace(poster))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                errors.Add(
                    new MetadataProviderError(
                        provider.Name,
                        exception.GetType().Name,
                        exception.Message));
            }
        }

        return new MetadataArtworkResolution(
            new MetadataArtwork(
                poster,
                backdrop,
                thumbnail ?? poster),
            providers,
            errors);
    }
}
