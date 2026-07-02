using System;

namespace JarvisCSharp.Utils;

/// <summary>
/// Utility class for normalizing transcription text for command detection.
/// Implements Requirements 4.1, 4.4, 17.3, 17.4, 27.5.
/// </summary>
public static class TranscriptionNormalizer
{
    /// <summary>
    /// Normalizes transcription text for command detection.
    /// - Trims whitespace
    /// - Converts to lowercase
    /// - Normalizes Turkish characters for flexible matching
    /// </summary>
    /// <param name="text">The raw transcription text to normalize</param>
    /// <returns>Normalized text, or empty string if input is null/empty</returns>
    public static string NormalizeTranscription(string text)
    {
        // Handle null and empty string cases (Requirement 4.1, 17.3)
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Trim whitespace and convert to lowercase (Requirement 4.1, 17.3)
        var normalized = text.Trim().ToLower();

        // Turkish character normalization (Requirement 4.4, 17.4, 27.5)
        // Maps Turkish characters to ASCII equivalents for flexible matching
        normalized = normalized.Replace('ı', 'i');  // Turkish dotless i → i
        normalized = normalized.Replace('İ', 'i');  // Turkish capital I with dot → i
        normalized = normalized.Replace('ş', 's');  // ş → s
        normalized = normalized.Replace('Ş', 's');  // Ş → s
        normalized = normalized.Replace('ğ', 'g');  // ğ → g
        normalized = normalized.Replace('Ğ', 'g');  // Ğ → g
        normalized = normalized.Replace('ü', 'u');  // ü → u
        normalized = normalized.Replace('Ü', 'u');  // Ü → u
        normalized = normalized.Replace('ö', 'o');  // ö → o
        normalized = normalized.Replace('Ö', 'o');  // Ö → o
        normalized = normalized.Replace('ç', 'c');  // ç → c
        normalized = normalized.Replace('Ç', 'c');  // Ç → c

        return normalized;
    }
}
