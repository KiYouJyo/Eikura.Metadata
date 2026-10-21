using System.Text.RegularExpressions;

namespace Eizo.Metadata.Recognition.Internal;

internal static class TechnicalSuffixAnalyzer
{
    private const RegexOptions Options =
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex TrailingReleaseGroupRegex = new(
        @"(?:^|[\s._])[-–—−](?<group>[A-Za-z][A-Za-z0-9._-]{1,31})\s*$",
        Options,
        Timeout);

    internal static TechnicalSuffixResult? Analyze(NormalizedMediaPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var tokens = path.Tokens;
        for (var i = 0; i < tokens.Count; i++)
        {
            var candidate = tokens[i];
            if (!IsPotentialStart(candidate) ||
                !CanStartSuffix(tokens, i) ||
                !HasMeaningfulContentBefore(tokens, i) ||
                CountStrongSignals(tokens, i) < 2)
            {
                continue;
            }

            var year = tokens
                .Skip(i)
                .FirstOrDefault(static token => token.Kind == TokenKind.Year);

            var evidence = new List<RecognitionEvidence>
            {
                new(
                    "technical-suffix.start",
                    candidate.NormalizedValue,
                    0.88),
            };

            var releaseGroup = TryGetTrailingReleaseGroup(
                path.Stem,
                candidate.Start);

            if (releaseGroup is not null)
            {
                evidence.Add(new RecognitionEvidence(
                    "release-group.trailing",
                    releaseGroup,
                    0.82));
            }

            return new TechnicalSuffixResult(
                candidate.Start,
                year,
                releaseGroup,
                evidence);
        }

        return null;
    }

    private static bool IsPotentialStart(RecognitionToken token) =>
        token.Kind is
            TokenKind.Year or
            TokenKind.Resolution or
            TokenKind.Source or
            TokenKind.VideoCodec or
            TokenKind.AudioCodec or
            TokenKind.BitDepth or
            TokenKind.TechnicalGroup;

    private static bool IsStrongSignal(RecognitionToken token) =>
        token.Kind is
            TokenKind.Resolution or
            TokenKind.Source or
            TokenKind.VideoCodec or
            TokenKind.AudioCodec or
            TokenKind.BitDepth or
            TokenKind.TechnicalGroup;

    private static bool CanStartSuffix(
        IReadOnlyList<RecognitionToken> tokens,
        int index)
    {
        if (tokens[index].Kind != TokenKind.Year)
        {
            return true;
        }

        // Preserve an early title-like year only when another plausible year
        // appears later before the technical tail, e.g. SAC_2045...2021.1080p.
        // A normal release year followed by an episode marker remains the
        // suffix boundary: Title 2021 第04話 1080p HDTV.
        for (var i = index + 1; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.Year)
            {
                return false;
            }

            if (IsStrongSignal(token))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountStrongSignals(
        IReadOnlyList<RecognitionToken> tokens,
        int start)
    {
        var count = 0;
        for (var i = start; i < tokens.Count; i++)
        {
            if (IsStrongSignal(tokens[i]))
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasMeaningfulContentBefore(
        IReadOnlyList<RecognitionToken> tokens,
        int index)
    {
        for (var i = 0; i < index; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.Separator ||
                token.IsBracketed &&
                ReleaseNoiseClassifier.IsTrailingNoiseTag(token))
            {
                continue;
            }

            if (token.NormalizedValue.Any(static c =>
                    char.IsLetter(c) ||
                    c is >= '\u3040' and <= '\u30ff' ||
                    c is >= '\u3400' and <= '\u4dbf' ||
                    c is >= '\u4e00' and <= '\u9fff' ||
                    c is >= '\uac00' and <= '\ud7af'))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryGetTrailingReleaseGroup(
        string stem,
        int technicalStart)
    {
        var match = TrailingReleaseGroupRegex.Match(stem);
        if (!match.Success || match.Index < technicalStart)
        {
            return null;
        }

        var value = match.Groups["group"].Value;
        return value.Length is >= 2 and <= 32 ? value : null;
    }
}
