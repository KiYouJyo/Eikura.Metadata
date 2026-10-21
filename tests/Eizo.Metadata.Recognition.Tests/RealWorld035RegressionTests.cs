namespace Eizo.Metadata.Recognition.Tests;

public sealed class RealWorld035RegressionTests
{
    private readonly RecognitionEngine _engine = new();

    [Fact]
    public void Recognize_GenericMovieDirectoryDoesNotOverrideStrongEpisodeIdentity()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/Example Band (2024)/Example Band (2024) S01E01.东京之夜.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("Example Band", result.Title);
        Assert.Equal("东京之夜", result.EpisodeTitle);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "media-kind.episode-overrides-movie-directory");
        Assert.DoesNotContain(result.Evidence, static item =>
            item.Code == "confidence.domain-episode-conflict");
    }

    [Fact]
    public void Recognize_DotDelimitedSxxExxSeparatesEpisodeTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/Example Guild (2009)/Example Guild (2009) S03E05.黑魔法师.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("Example Guild", result.Title);
        Assert.Equal("黑魔法师", result.EpisodeTitle);
        Assert.Equal(3, result.SeasonNumber);
        Assert.Equal(5m, result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_YearAfterEpisodeIsNotConsumedAsDecimalEpisode()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Anime/Example/S1/Example.S01E09.2012.2160P.mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(9m, result.EpisodeNumber);
        Assert.Equal(2012, result.Year);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode.year-boundary-correction");
    }

    [Fact]
    public void Recognize_ReleaseGroupThenBracketEpisodeUsesEpisodeInsteadOfMovieDirectory()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/Example Show/[Ygm] Example Show [01][Ma10p_2160P][x265_flac_aac].mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("Example Show", result.Title);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.DoesNotContain("Ma10p", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode.bracket-after-title");
    }

    [Fact]
    public void Recognize_FourDigitSxxExxSupportsLongRunningSeries()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/Long Running Show/1001-1100/Long Running Show.1996.S01E1001.mp4"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("Long Running Show", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1001m, result.EpisodeNumber);
        Assert.Contains(result.Evidence, static item =>
            item.Code == "episode.sxxexx.extended");
    }

    [Fact]
    public void Recognize_LowValueInstructionDirectoryDoesNotBecomeSeriesTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/Classic Series/经典系列part1/公众号：Notes/01.mp4"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.NotNull(result.Title);
        Assert.DoesNotContain("公众号", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Notes", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recognize_CompositeTechnicalBracketDoesNotPolluteTitle()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Example Journey S03E01 [IQIYI WebRip 2160p NVENC AAC Multi-Subs].mkv"));

        Assert.Equal(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Equal("Example Journey", result.Title);
        Assert.Equal(1m, result.EpisodeNumber);
        Assert.DoesNotContain("IQIYI", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NVENC", result.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Multi", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recognize_TitleYearLikeTokenBeforeLexicalTextIsPreserved()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Project_2045.Sustainable.Future.2021.1080p.BluRay.x265.10bit-GRP.mkv"));

        Assert.Equal(2021, result.Year);
        Assert.NotNull(result.Title);
        Assert.Contains("2045", result.Title, StringComparison.Ordinal);
        Assert.Contains("Sustainable", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recognize_ExplicitMovieMarkerStillWinsOverEpisodeLookingText()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "movie/MOVIE - Example S01E01.mkv"));

        Assert.Equal(MediaKind.Movie, result.MediaKind);
        Assert.Null(result.EpisodeNumber);
    }

    [Fact]
    public void Recognize_NonTechnicalBracketTailDoesNotInventEpisode()
    {
        var result = _engine.Recognize(
            new RecognitionRequest(
                "Project Notes [34][Draft][Review].mkv"));

        Assert.NotEqual(MediaKind.SeriesEpisode, result.MediaKind);
        Assert.Null(result.EpisodeNumber);
    }
}
