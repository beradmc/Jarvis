using System;
using System.Linq;
using JarvisCSharp.Config;
using JarvisCSharp.Models;

namespace JarvisCSharp.Utils;

/// <summary>
/// Utility class for detecting mode commands and wake words in transcription text.
/// Implements Requirements 2.1-2.4, 4.2-4.6, 8.1-8.6, 24.1-24.7.
/// </summary>
public static class CommandDetector
{
    /// <summary>
    /// Detects commands in transcription text with priority checking.
    /// Priority order: sleep commands, unmute commands, wake words.
    /// Uses word boundary detection to avoid substring false positives.
    /// Handles Turkish and English command variations.
    /// </summary>
    /// <param name="text">The transcription text to analyze</param>
    /// <returns>CommandDetectionResult enum value indicating detected command type</returns>
    public static CommandDetectionResult DetectCommandInTranscription(string text)
    {
        // Normalize the input text (Requirement 4.1, 4.4, 17.3, 17.4)
        var normalizedText = TranscriptionNormalizer.NormalizeTranscription(text);
        
        // Handle empty input (Requirement 24.1)
        if (string.IsNullOrWhiteSpace(normalizedText))
            return CommandDetectionResult.NO_COMMAND;

        // Priority 1: Check for sleep commands (Requirement 8.1, 8.2, 8.3, 24.4)
        // Sleep commands have highest priority to prevent "jarvis kapat" from being interpreted as wake word
        foreach (var sleepCommand in ModeConfiguration.SLEEP_COMMANDS)
        {
            var normalizedCommand = TranscriptionNormalizer.NormalizeTranscription(sleepCommand);
            if (ContainsWithWordBoundary(normalizedText, normalizedCommand))
            {
                return CommandDetectionResult.SLEEP_COMMAND;
            }
        }

        // Priority 2: Check for mute commands (keep listening, stop responding)
        foreach (var muteCommand in ModeConfiguration.MUTE_COMMANDS)
        {
            var normalizedCommand = TranscriptionNormalizer.NormalizeTranscription(muteCommand);
            if (ContainsWithWordBoundary(normalizedText, normalizedCommand))
            {
                return CommandDetectionResult.MUTE_COMMAND;
            }
        }

        // Priority 3: Check for unmute commands (Requirement 24.4)
        // Note: "jarvis konuş" and "jarvis speak" should unmute, but plain "jarvis" should be wake word
        var unmuteCommands = new[]
        {
            "jarvis konus",  // Turkish: "Jarvis speak" (normalized from "jarvis konuş")
            "jarvis speak",
            "jarvis acil"    // Turkish: "Jarvis open up" (normalized from "jarvis açıl")
        };

        foreach (var unmuteCommand in unmuteCommands)
        {
            if (ContainsWithWordBoundary(normalizedText, unmuteCommand))
            {
                return CommandDetectionResult.UNMUTE_COMMAND;
            }
        }

        // Priority 3: Check for wake words (Requirement 4.2, 4.3, 24.4)
        // Wake words have lowest priority so they don't interfere with commands
        foreach (var wakeWord in ModeConfiguration.WAKE_WORDS)
        {
            var normalizedWakeWord = TranscriptionNormalizer.NormalizeTranscription(wakeWord);
            
            // Skip multi-word wake phrases that contain command keywords
            // These are handled as unmute commands above
            if (normalizedWakeWord.Contains("konus") || 
                normalizedWakeWord.Contains("speak") || 
                normalizedWakeWord.Contains("acil"))
            {
                continue;
            }
            
            if (ContainsWithWordBoundary(normalizedText, normalizedWakeWord))
            {
                return CommandDetectionResult.WAKE_WORD;
            }
        }

        // No command detected (Requirement 24.2)
        return CommandDetectionResult.NO_COMMAND;
    }

    /// <summary>
    /// Checks if text contains a phrase with word boundary detection.
    /// Prevents false positives from substring matches (e.g., "jarvislover" should not match "jarvis").
    /// Implements Requirement 4.6, 24.6.
    /// </summary>
    /// <param name="text">The text to search in (must be normalized)</param>
    /// <param name="phrase">The phrase to search for (must be normalized)</param>
    /// <returns>True if phrase is found with proper word boundaries, false otherwise</returns>
    private static bool ContainsWithWordBoundary(string text, string phrase)
    {
        // Handle exact match (Requirement 24.2)
        if (text == phrase)
            return true;

        // Find all occurrences of the phrase
        var index = text.IndexOf(phrase, StringComparison.Ordinal);
        
        while (index >= 0)
        {
            // Check if this is a word boundary match
            var isStartBoundary = index == 0 || IsWordBoundaryChar(text[index - 1]);
            var endIndex = index + phrase.Length;
            var isEndBoundary = endIndex >= text.Length || IsWordBoundaryChar(text[endIndex]);

            // If both boundaries are valid, we have a match (Requirement 4.6)
            if (isStartBoundary && isEndBoundary)
                return true;

            // Look for next occurrence
            index = text.IndexOf(phrase, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    /// <summary>
    /// Determines if a character is a word boundary character.
    /// Word boundaries include: space, punctuation, start/end of string.
    /// </summary>
    /// <param name="c">The character to check</param>
    /// <returns>True if character is a word boundary, false otherwise</returns>
    private static bool IsWordBoundaryChar(char c)
    {
        // Word boundaries: space, punctuation, symbols
        return char.IsWhiteSpace(c) || 
               char.IsPunctuation(c) || 
               char.IsSymbol(c) ||
               c == ',' || c == '.' || c == '!' || c == '?' || c == ';' || c == ':';
    }
}
