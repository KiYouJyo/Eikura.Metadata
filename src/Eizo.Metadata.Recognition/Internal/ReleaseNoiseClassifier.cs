namespace Eizo.Metadata.Recognition.Internal;

internal static class ReleaseNoiseClassifier
{
    private static readonly string[] ProviderPrefixes =
    {
        "BILIBILI",
        "BGLOBAL",
        "CRUNCHYROLL",
        "NETFLIX",
        "AMAZON",
        "AMZN",
        "BAHA",
        "ABEMA",
        "HULU",
        "DISNEY",
        "IQIYI",
        "YOUKU",
        "TENCENT",
    };

    internal static bool IsTrailingNoiseTag(RecognitionToken token)
    {
        if (!token.IsBracketed)
        {
            return false;
        }

        if (token.Kind is
            TokenKind.ReleaseGroup or
            TokenKind.Resolution or
            TokenKind.Source or
            TokenKind.VideoCodec or
            TokenKind.AudioCodec or
            TokenKind.BitDepth or
            TokenKind.Language or
            TokenKind.Checksum or
            TokenKind.TechnicalGroup)
        {
            return true;
        }

        return IsProviderMetadataTag(token);
    }

    internal static bool IsIdentityTag(RecognitionToken token)
    {
        if (!token.IsBracketed)
        {
            return false;
        }

        return token.Kind is TokenKind.ReleaseGroup or TokenKind.Source ||
               IsProviderMetadataTag(token);
    }

    internal static bool IsProviderMetadataTag(RecognitionToken token)
    {
        if (!token.IsBracketed ||
            token.Kind is not (TokenKind.BracketGroup or TokenKind.TechnicalGroup))
        {
            return false;
        }

        var value = token.NormalizedValue;
        if (value.Length is < 3 or > 80 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        if (!value.All(static c =>
            char.IsAsciiLetterOrDigit(c) ||
            c is '_' or '-' or '.' or '+'))
        {
            return false;
        }

        var key = value.ToUpperInvariant();
        return ProviderPrefixes.Any(prefix =>
            key.Equals(prefix, StringComparison.Ordinal) ||
            key.StartsWith(prefix + "_", StringComparison.Ordinal) ||
            key.StartsWith(prefix + "-", StringComparison.Ordinal) ||
            key.StartsWith(prefix + ".", StringComparison.Ordinal));
    }
}
