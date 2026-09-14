using System.Net;
using System.Text;
using Eizo.Metadata.Core;
using Eizo.Metadata.Providers;
using Eizo.Metadata.Recognition;

namespace Eizo.Metadata.Providers.Tests;

public sealed class ProviderContractTests
{
    [Fact]
    public async Task Bangumi_SearchUsesPublicV0ContractAndMapsCandidate()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("/v0/search/subjects", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("limit=50", request.RequestUri.Query, StringComparison.Ordinal);
            Assert.Contains("KiYouJyo/Eizo", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);

            return Json("""
                {
                  "data": [
                    {
                      "id": 253,
                      "type": 2,
                      "name": "攻殻機動隊 STAND ALONE COMPLEX",
                      "name_cn": "攻壳机动队 STAND ALONE COMPLEX",
                      "date": "2002-10-01",
                      "platform": "TV",
                      "rating": { "score": 8.8 }
                    }
                  ],
                  "total": 1,
                  "limit": 10,
                  "offset": 0
                }
                """);
        });

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.6 (https://github.com/KiYouJyo/Eizo)",
                SearchAliasEnrichmentLimit: 0));

        var result = await provider.SearchAsync(
            new MetadataSearchRequest(
                ["攻殻機動隊"],
                2002,
                MediaKind.SeriesEpisode,
                1,
                1,
                "zh-CN",
                10), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(result);
        Assert.Equal("253", candidate.Id.Value);
        Assert.Equal(MetadataSubjectKind.Series, candidate.Id.Kind);
        Assert.Equal("攻壳机动队 STAND ALONE COMPLEX", candidate.Titles.Primary);
        Assert.Equal(2002, candidate.Year);
        Assert.Equal(MetadataContentKind.Animation, candidate.ContentKind);
    }


    [Fact]
    public async Task Bangumi_SearchRunsAllStrongQueryVariantsBeforeTruncation()
    {
        var postCount = 0;
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            postCount++;

            return Json("""
                {
                  "data": [
                    {
                      "id": 100,
                      "name": "LUPIN THE IIIRD",
                      "name_cn": "鲁邦三世",
                      "date": "1977-10-03",
                      "platform": "TV"
                    },
                    {
                      "id": 101,
                      "name": "LUPIN THE IIIRD PART2",
                      "name_cn": "鲁邦三世 PART2",
                      "date": "1977-10-03",
                      "platform": "TV"
                    }
                  ],
                  "total": 2,
                  "limit": 2,
                  "offset": 0
                }
                """);
        });

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.6 (https://github.com/KiYouJyo/Eizo)",
                SearchAliasEnrichmentLimit: 0));

        _ = await provider.SearchAsync(
            new MetadataSearchRequest(
                ["02.[1977-1980]鲁邦三世part2", "鲁邦三世part2"],
                null,
                MediaKind.SeriesEpisode,
                null,
                1,
                "zh-CN",
                2),
            TestContext.Current.CancellationToken);

        Assert.Equal(5, postCount);
    }

    [Fact]
    public async Task Bangumi_SearchEnrichesTopCandidateWithInfoboxAliases()
    {
        var handler = new RecordingHandler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return Json("""
                    {
                      "data": [
                        {
                          "id": 1428,
                          "name": "鋼の錬金術師 FULLMETAL ALCHEMIST",
                          "name_cn": "钢之炼金术师 FULLMETAL ALCHEMIST",
                          "date": "2009-04-05",
                          "platform": "TV",
                          "rating": { "score": 9.0 }
                        }
                      ],
                      "total": 1,
                      "limit": 10,
                      "offset": 0
                    }
                    """);
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("/v0/subjects/1428", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Json("""
                {
                  "id": 1428,
                  "name": "鋼の錬金術師 FULLMETAL ALCHEMIST",
                  "name_cn": "钢之炼金术师 FULLMETAL ALCHEMIST",
                  "date": "2009-04-05",
                  "platform": "TV",
                  "summary": "",
                  "eps": 64,
                  "infobox": [
                    {
                      "key": "别名",
                      "value": [
                        { "k": "英文名", "v": "Fullmetal Alchemist: Brotherhood" }
                      ]
                    }
                  ]
                }
                """);
        });

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.6 (https://github.com/KiYouJyo/Eizo)",
                SearchAliasEnrichmentLimit: 1));

        var result = await provider.SearchAsync(
            new MetadataSearchRequest(
                ["Fullmetal Alchemist: Brotherhood"],
                2009,
                MediaKind.SeriesEpisode,
                null,
                1,
                "en",
                10),
            TestContext.Current.CancellationToken);

        var candidate = Assert.Single(result);
        Assert.Contains(
            "Fullmetal Alchemist: Brotherhood",
            candidate.Titles.Aliases,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bangumi_GetRelatedSubjectsMapsSequelRelation()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains(
                "/v0/subjects/245665/subjects",
                request.RequestUri!.AbsolutePath,
                StringComparison.Ordinal);

            return Json("""
                [
                  {
                    "id": 350764,
                    "type": 2,
                    "name": "鬼滅の刃 無限列車編",
                    "name_cn": "鬼灭之刃 无限列车篇",
                    "relation": "续集"
                  },
                  {
                    "id": 291494,
                    "type": 2,
                    "name": "劇場版 鬼滅の刃 無限列車編",
                    "name_cn": "剧场版 鬼灭之刃 无限列车篇",
                    "relation": "不同演绎"
                  }
                ]
                """);
        });

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.9 (https://github.com/KiYouJyo/Eizo)",
                SearchAliasEnrichmentLimit: 0));

        var relations = await provider.GetRelatedSubjectsAsync(
            new MetadataProviderItemId(
                "bangumi",
                "245665",
                MetadataSubjectKind.Series),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, relations.Count);
        var sequel = Assert.Single(
            relations,
            static item => item.Relation == "续集");
        Assert.Equal("350764", sequel.SubjectId.Value);
        Assert.Equal("鬼灭之刃 无限列车篇", sequel.Titles.Primary);
    }

    [Fact]
    public async Task Bangumi_GetSubjectClassifiesLiveAction()
    {
        var handler = new RecordingHandler(_ =>
            Json("""
                {
                  "id": 500,
                  "type": 6,
                  "name": "ドラゴン桜",
                  "name_cn": "龙樱",
                  "date": "2005-07-08",
                  "platform": "电视剧",
                  "eps": 11
                }
                """));

        var provider = new BangumiMetadataProvider(
            new HttpClient(handler),
            new BangumiMetadataProviderOptions(
                "KiYouJyo/Eizo/0.3.10 (https://github.com/KiYouJyo/Eizo)",
                SearchAliasEnrichmentLimit: 0));

        var subject = await provider.GetSubjectAsync(
            new MetadataProviderItemId(
                "bangumi",
                "500",
                MetadataSubjectKind.Series),
            TestContext.Current.CancellationToken);

        Assert.NotNull(subject);
        Assert.Equal(MetadataContentKind.LiveAction, subject.ContentKind);
    }

    [Fact]
    public async Task Tmdb_SearchUsesBearerTokenAndMapsTvCandidate()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
            Assert.Contains("/3/search/tv", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("first_air_date_year=2011", request.RequestUri.Query, StringComparison.Ordinal);

            return Json("""
                {
                  "results": [
                    {
                      "id": 42509,
                      "name": "Steins;Gate",
                      "original_name": "STEINS;GATE",
                      "first_air_date": "2011-04-06",
                      "genre_ids": [16, 10765],
                      "poster_path": "/poster.jpg",
                      "backdrop_path": "/backdrop.jpg",
                      "popularity": 42.0
                    }
                  ]
                }
                """);
        });

        var provider = new TmdbMetadataProvider(
            new HttpClient(handler),
            new TmdbMetadataProviderOptions("secret-token", "en-US"));

        var result = await provider.SearchAsync(
            new MetadataSearchRequest(
                ["Steins;Gate"],
                2011,
                MediaKind.SeriesEpisode,
                1,
                1,
                "en",
                10), TestContext.Current.CancellationToken);

        var candidate = Assert.Single(result);
        Assert.Equal("42509", candidate.Id.Value);
        Assert.Equal(MetadataSubjectKind.Series, candidate.Id.Kind);
        Assert.Equal(2011, candidate.Year);
        Assert.Equal(MetadataContentKind.Animation, candidate.ContentKind);
    }

    [Fact]
    public async Task Tmdb_GetSubjectMapsExternalIdsAndArtwork()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Contains("/3/movie/129", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("append_to_response=external_ids", request.RequestUri.Query, StringComparison.Ordinal);

            return Json("""
                {
                  "id": 129,
                  "title": "千与千寻",
                  "original_title": "千と千尋の神隠し",
                  "overview": "A story.",
                  "release_date": "2001-07-20",
                  "poster_path": "/poster.jpg",
                  "backdrop_path": "/backdrop.jpg",
                  "genres": [
                    { "id": 16, "name": "Animation" },
                    { "id": 14, "name": "Fantasy" }
                  ],
                  "external_ids": {
                    "imdb_id": "tt0245429"
                  }
                }
                """);
        });

        var provider = new TmdbMetadataProvider(
            new HttpClient(handler),
            new TmdbMetadataProviderOptions("secret-token", "zh-CN"));

        var subject = await provider.GetSubjectAsync(
            new MetadataProviderItemId("tmdb", "129", MetadataSubjectKind.Movie),
            TestContext.Current.CancellationToken);

        Assert.NotNull(subject);
        Assert.Equal("千与千寻", subject.Titles.Primary);
        Assert.Equal("tt0245429", subject.ExternalIds["imdb"]);
        Assert.Contains("/poster.jpg", subject.Artwork.PosterUrl, StringComparison.Ordinal);
        Assert.Equal(MetadataContentKind.Animation, subject.ContentKind);
    }

    [Fact]
    public async Task AniListArtwork_MapsBannerAndCoverForAnime()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("graphql.anilist.co", request.RequestUri!.Host);

            var body = request.Content!
                .ReadAsStringAsync()
                .GetAwaiter()
                .GetResult();

            using var requestJson =
                System.Text.Json.JsonDocument.Parse(body);
            Assert.Equal(
                "葬送的芙莉莲",
                requestJson.RootElement
                    .GetProperty("variables")
                    .GetProperty("search")
                    .GetString());
            Assert.Contains(
                "bannerImage",
                requestJson.RootElement
                    .GetProperty("query")
                    .GetString(),
                StringComparison.Ordinal);

            return Json("""
                {
                  "data": {
                    "Page": {
                      "media": [
                        {
                          "id": 154587,
                          "idMal": 52991,
                          "title": {
                            "romaji": "Sousou no Frieren",
                            "english": "Frieren: Beyond Journey's End",
                            "native": "葬送のフリーレン"
                          },
                          "synonyms": ["葬送的芙莉莲"],
                          "startDate": { "year": 2023 },
                          "bannerImage": "https://img.anilist.co/banner/frieren.jpg",
                          "coverImage": {
                            "extraLarge": "https://img.anilist.co/cover/frieren.jpg",
                            "large": null
                          },
                          "popularity": 500000
                        }
                      ]
                    }
                  }
                }
                """);
        });

        var provider = new AniListArtworkProvider(
            new HttpClient(handler));

        var artwork = await provider.ResolveArtworkAsync(
            new MetadataArtworkRequest(
                ["葬送的芙莉莲", "葬送のフリーレン"],
                2023,
                MetadataSubjectKind.Series,
                MetadataContentKind.Animation,
                "zh-CN",
                new Dictionary<string, string>()),
            TestContext.Current.CancellationToken);

        Assert.NotNull(artwork);
        Assert.Equal(
            "https://img.anilist.co/banner/frieren.jpg",
            artwork.BackdropUrl);
        Assert.Equal(
            "https://img.anilist.co/cover/frieren.jpg",
            artwork.PosterUrl);
    }

    [Fact]
    public async Task TmdbArtwork_UsesDirectTmdbIdForBackdrop()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Contains(
                "/3/tv/42509",
                request.RequestUri!.AbsolutePath,
                StringComparison.Ordinal);

            return Json("""
                {
                  "id": 42509,
                  "name": "Steins;Gate",
                  "original_name": "STEINS;GATE",
                  "overview": "",
                  "first_air_date": "2011-04-06",
                  "number_of_episodes": 24,
                  "poster_path": "/poster.jpg",
                  "backdrop_path": "/backdrop.jpg",
                  "genres": [
                    { "id": 16, "name": "Animation" }
                  ],
                  "external_ids": {}
                }
                """);
        });

        var provider = new TmdbMetadataProvider(
            new HttpClient(handler),
            new TmdbMetadataProviderOptions(
                "secret-token",
                "zh-CN"));

        var artwork = await provider.ResolveArtworkAsync(
            new MetadataArtworkRequest(
                ["Steins;Gate"],
                2011,
                MetadataSubjectKind.Series,
                MetadataContentKind.Animation,
                "zh-CN",
                new Dictionary<string, string>
                {
                    ["tmdb"] = "42509",
                }),
            TestContext.Current.CancellationToken);

        Assert.NotNull(artwork);
        Assert.Contains(
            "/backdrop.jpg",
            artwork.BackdropUrl,
            StringComparison.Ordinal);
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
