using System;
using System.Text.RegularExpressions;

namespace WisprClone.App.PostProcess;

/// <summary>
/// Light, rules-based polish applied to raw Whisper output before injection.
/// Cheap and predictable — no LLM cleanup pass in v1.
/// </summary>
public static class Cleanup
{
    // Match standalone English fillers as whole words, case-insensitive.
    // Whisper often transcribes mouth-closed fillers ("um", "uh") as "Mmm",
    // "mmm", "hmm" etc., so we cover those phonetic variants too.
    // The surrounding [\s,]* eats commas Whisper inserts around fillers,
    // otherwise stripping "mmm" out of "Mmm, hello, mmm, world?" leaves
    // stray commas behind.
    private static readonly Regex FillerRegex =
        new(@"[\s,]*\b(um+|uh+|erm+|ehm+|hm+|mm+)\b[\s,]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MultiSpaceRegex =
        new(@"\s{2,}", RegexOptions.Compiled);

    // Strip leading punctuation/whitespace left over after filler removal
    // at the start of the sentence (e.g. ", hello world" → "hello world").
    private static readonly Regex LeadingPunctRegex =
        new(@"^[,\.\s]+", RegexOptions.Compiled);

    public static string Apply(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var s = text.Trim();

        // Strip filler words and any commas/whitespace immediately around them.
        // Replace with a single space so adjacent words don't collide together.
        s = FillerRegex.Replace(s, " ");

        // Collapse any runs of whitespace introduced by the substitution.
        s = MultiSpaceRegex.Replace(s, " ");

        // Strip leftover leading punctuation (commas/periods Whisper put before
        // a filler that we removed).
        s = LeadingPunctRegex.Replace(s, "");

        // Trim residual outer whitespace.
        s = s.Trim();

        // Capitalize first letter for Latin-script starts. char.ToUpper on
        // Hebrew / non-cased scripts is a no-op, so safe to always call.
        if (s.Length > 0 && char.IsLetter(s[0]))
        {
            s = char.ToUpper(s[0]) + s[1..];
        }

        // Append a trailing space so the user can immediately continue typing
        // (or dictating a follow-up sentence) without manually adding one.
        if (!s.EndsWith(' '))
        {
            s += ' ';
        }

        return s;
    }
}
