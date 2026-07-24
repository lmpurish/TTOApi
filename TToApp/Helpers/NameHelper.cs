using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TToApp.Helpers;

public static class NameHelper
{
    // Quita acentos, caracteres especiales, colapsa espacios y pasa a minúsculas
    public static string NormName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";

        s = s.Trim();
        s = Regex.Replace(s, @"[^\p{L}\p{N}\s]", " ");
        s = Regex.Replace(s, @"\s+", " ");
        s = s.ToLowerInvariant();

        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    // Normaliza un nombre completo; soporta formato "Apellido, Nombre"
    public static string NormalizeDriverFullName(string raw)
    {
        raw = raw?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw)) return "";

        if (raw.Contains(','))
        {
            var parts = raw.Split(',', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2)
                raw = $"{parts[1].Trim()} {parts[0].Trim()}";
        }

        return NormName(raw);
    }

    // Devuelve solo el primer y último token (quita nombres del medio)
    public static string RemoveMiddleName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "";
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length <= 2 ? fullName : $"{parts[0]} {parts[^1]}";
    }

    // Busca userId en el diccionario con dos intentos: nombre completo y sin nombre del medio
    public static bool TryFindByName(Dictionary<string, List<int>> dict, string rawName, out int userId)
    {
        userId = 0;
        var key = NormalizeDriverFullName(rawName);
        if (string.IsNullOrWhiteSpace(key)) return false;

        if (dict.TryGetValue(key, out var ids) && ids.Count == 1)
        {
            userId = ids[0];
            return true;
        }

        var withoutMiddle = NormName(RemoveMiddleName(rawName));
        if (dict.TryGetValue(withoutMiddle, out var ids2) && ids2.Count == 1)
        {
            userId = ids2[0];
            return true;
        }

        return false;
    }
}
