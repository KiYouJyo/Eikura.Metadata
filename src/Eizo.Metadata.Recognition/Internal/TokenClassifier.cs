using System.Text;

namespace Eizo.Metadata.Recognition.Internal;

internal static class TokenClassifier
{
    private static readonly HashSet<string> Sources = new(StringComparer.Ordinal)
    {
        "WEB", "WEBDL", "WEBRIP", "BLURAY", "BDRIP", "BDREMUX", "HDTV",
        "DVDRIP", "REMUX", "BAHA", "NETFLIX", "NF", "AMZN", "CR", "ATX",
        "IQIYI", "YOUKU", "TENCENT", "DISNEYPLUS", "BILIBILI",
    };

    private static readonly HashSet<string> VideoCodecs = new(StringComparer.Ordinal)
    {
        "AVC", "HEVC", "H264", "H265", "X264", "X265", "AV1", "VP9", "MPEG2",
    };

    private static readonly HashSet<string> AudioCodecs = new(StringComparer.Ordinal)
    {
        "AAC", "FLAC", "AC3", "EAC3", "DTS", "TRUEHD", "DOLBY", "ATMOS",
        "OPUS", "MP3", "LPCM",
    };

    private static readonly HashSet<string> Languages = new(StringComparer.Ordinal)
    {
        "CHT", "CHS", "JPN", "JAP", "JA", "ENG", "EN", "BIG5", "GB", "GBK",
        "SC", "TC", "简中", "繁中", "簡中", "繁體", "简体", "CHTC",
    };

    private static readonly HashSet<string> ReleaseGroups = new(StringComparer.Ordinal)
    {
        "ANI", "LILITHRAWS", "NCRAWS", "LOLIHOUSE", "REINFORCE", "MOOZZI2",
        "NEKOMOEKISSATEN", "DBDRAWS",
    };

    private static readonly HashSet<string> TechnicalQualifiers = new(StringComparer.Ordinal)
    {
        "NVENC", "MULTI", "SUB", "SUBS", "MULTISUB", "MULTISUBS", "DUAL",
        "DUALAUDIO", "HDR", "HDR10", "HDR10PLUS", "DV", "DOVI", "MA10P",
        "ASS", "SRT", "PGS",
    };

    internal static TokenKind Classify(string value, bool bracketed)
    {
        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return bracketed ? TokenKind.BracketGroup : TokenKind.Text;
        }

        var atomic = ClassifyAtomic(normalized, bracketed);
        if (atomic != TokenKind.Text)
        {
            return atomic;
        }

        if (bracketed && IsTechnicalGroup(normalized))
        {
            return TokenKind.TechnicalGroup;
        }

        return bracketed ? TokenKind.BracketGroup : TokenKind.Text;
    }

    internal static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormKC).Trim();

    private static TokenKind ClassifyAtomic(string normalized, bool bracketed)
    {
        var key = ToKey(normalized);

        if (ReleaseGroups.Contains(key))
        {
            return TokenKind.ReleaseGroup;
        }

        if (Sources.Contains(key))
        {
            return TokenKind.Source;
        }

        if (VideoCodecs.Contains(key))
        {
            return TokenKind.VideoCodec;
        }

        if (AudioCodecs.Contains(key))
        {
            return TokenKind.AudioCodec;
        }

        if (Languages.Contains(key))
        {
            return TokenKind.Language;
        }

        if (TechnicalQualifiers.Contains(key))
        {
            return TokenKind.TechnicalGroup;
        }

        if (IsResolution(key))
        {
            return TokenKind.Resolution;
        }

        if (IsBitDepth(key))
        {
            return TokenKind.BitDepth;
        }

        if (IsYear(normalized))
        {
            return TokenKind.Year;
        }

        if (bracketed && IsChecksum(normalized))
        {
            return TokenKind.Checksum;
        }

        if (normalized.All(char.IsDigit))
        {
            return TokenKind.Number;
        }

        return TokenKind.Text;
    }

    private static bool IsTechnicalGroup(string value)
    {
        var parts = value.Split(
            new[] { ' ', '\t', ',', '+', '/', ';', '-', '_', '.' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2)
        {
            return false;
        }

        foreach (var part in parts)
        {
            var kind = ClassifyAtomic(Normalize(part), bracketed: true);
            if (kind is TokenKind.Text or TokenKind.Number or TokenKind.Year or TokenKind.ReleaseGroup)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsResolution(string key)
    {
        if (key is "4K" or "8K" or "UHD" or "FHD")
        {
            return true;
        }

        if (!key.EndsWith('P') || key.Length < 2)
        {
            return false;
        }

        return int.TryParse(key[..^1], out var height) &&
               height is 360 or 480 or 576 or 720 or 1080 or 1440 or 2160 or 4320;
    }

    private static bool IsBitDepth(string key)
    {
        if (!key.EndsWith("BIT", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(key[..^3], out var bits) && bits is 8 or 10 or 12 or 16;
    }

    private static bool IsYear(string value)
    {
        if (value.Length != 4 || !int.TryParse(value, out var year))
        {
            return false;
        }

        return year is >= 1900 and <= 2099;
    }

    private static bool IsChecksum(string value)
    {
        var compact = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compact.Length is not (8 or 32 or 40 or 64))
        {
            return false;
        }

        return compact.All(static c =>
            c is >= '0' and <= '9' ||
            c is >= 'a' and <= 'f' ||
            c is >= 'A' and <= 'F');
    }

    private static string ToKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Normalize(NormalizationForm.FormKC).ToUpperInvariant())
        {
            if (char.IsWhiteSpace(c) || c is '-' or '_' or '.')
            {
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
