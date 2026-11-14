using System;
using UnityEngine;

/// <summary>Single source of truth for the student application's server base URL.</summary>
public static class SerenaServerConfig
{
    public const string PlayerPrefsKey = "SerenaServerUrl";
    public const string DefaultServerUrl = "http://192.168.1.18:5080";

    public static string BaseUrl
    {
        get
        {
            string storedValue = PlayerPrefs.GetString(PlayerPrefsKey, DefaultServerUrl);
            return TryNormalize(storedValue, out string normalized, out _, out _)
                ? normalized
                : DefaultServerUrl;
        }
    }

    public static bool TrySave(string value, out string normalized, out string message)
    {
        if (!TryNormalize(value, out normalized, out string warning, out message))
            return false;

        PlayerPrefs.SetString(PlayerPrefsKey, normalized);
        PlayerPrefs.Save();
        message = string.IsNullOrEmpty(warning)
            ? "Settings saved."
            : "Settings saved. " + warning;
        return true;
    }

    public static bool TryNormalize(string value, out string normalized, out string warning, out string error)
    {
        normalized = string.Empty;
        warning = string.Empty;
        error = string.Empty;

        string candidate = (value ?? string.Empty).Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri))
        {
            error = "Enter an absolute URL, for example http://server:5080.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            error = "The client is set up for HTTP. The URL must start with http://.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "The URL must include the server name or address.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "Do not include a username or password in the URL.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "Enter the base address, without parameters or a fragment.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
        {
            error = "Enter only the server base address, without extra paths.";
            return false;
        }

        bool hasExplicitPort = HasExplicitPort(candidate);
        if (hasExplicitPort && (uri.Port < 1 || uri.Port > 65535))
        {
            error = "The port must be between 1 and 65535.";
            return false;
        }

        normalized = candidate.TrimEnd('/');
        if (!hasExplicitPort)
        {
            warning = "The URL has no port. If the server needs one, add it; none was added.";
        }

        return true;
    }

    private static bool HasExplicitPort(string value)
    {
        int schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return false;

        int authorityStart = schemeEnd + 3;
        int authorityEnd = value.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
        if (authorityEnd < 0)
            authorityEnd = value.Length;

        string authority = value.Substring(authorityStart, authorityEnd - authorityStart);
        if (authority.StartsWith("[", StringComparison.Ordinal))
        {
            int closingBracket = authority.IndexOf(']');
            return closingBracket >= 0 && closingBracket + 1 < authority.Length
                && authority[closingBracket + 1] == ':';
        }

        return authority.LastIndexOf(':') > 0;
    }
}
