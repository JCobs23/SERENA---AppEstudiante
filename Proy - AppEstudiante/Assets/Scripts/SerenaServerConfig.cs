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
            ? "Configuración guardada."
            : "Configuración guardada. " + warning;
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
            error = "Escribe una URL absoluta, por ejemplo http://servidor:5080.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            error = "El cliente está configurado para HTTP. La URL debe comenzar con http://.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "La URL debe incluir el nombre o la dirección del servidor.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "No incluyas usuario ni contraseña en la URL.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "Introduce la dirección base, sin parámetros ni fragmento.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
        {
            error = "Introduce solo la dirección base del servidor, sin rutas adicionales.";
            return false;
        }

        bool hasExplicitPort = HasExplicitPort(candidate);
        if (hasExplicitPort && (uri.Port < 1 || uri.Port > 65535))
        {
            error = "El puerto debe estar entre 1 y 65535.";
            return false;
        }

        normalized = candidate.TrimEnd('/');
        if (!hasExplicitPort)
        {
            warning = "La URL no incluye puerto. Si el servidor requiere uno, especifícalo; no se añadió ninguno.";
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
