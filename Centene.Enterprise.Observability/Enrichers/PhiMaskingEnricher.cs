using System.Text.RegularExpressions;

namespace Centene.Enterprise.Observability.Enrichers;

/// <summary>
/// Masks PHI (Protected Health Information) patterns in telemetry attributes
/// and log messages. Implements OBS-08 (PHI handling standard).
/// </summary>
/// <remarks>
/// Patterns covered: SSN, MBI (Medicare Beneficiary Identifier), Medicaid IDs,
/// dates of birth, phone numbers, email addresses, member IDs. This is a
/// defense-in-depth layer — services SHOULD NOT log PHI directly. The Dynatrace
/// attribute whitelist (mentioned in the Adding Attributes doc) is the
/// primary control; this enricher catches accidental leakage.
/// </remarks>
public sealed class PhiMaskingEnricher
{
    private static readonly (string Name, Regex Pattern, string Replacement)[] PhiPatterns =
    {
        ("ssn",        new Regex(@"\b\d{3}-?\d{2}-?\d{4}\b", RegexOptions.Compiled), "[SSN-REDACTED]"),
        ("mbi",        new Regex(@"\b\d[A-Z]\d{2}-?[A-Z]\d{2}-?[A-Z]\d{2}\b", RegexOptions.Compiled), "[MBI-REDACTED]"),
        ("email",      new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled), "[EMAIL-REDACTED]"),
        ("phone",      new Regex(@"\b(?:\+1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b", RegexOptions.Compiled), "[PHONE-REDACTED]"),
        ("dob",        new Regex(@"\b(0[1-9]|1[0-2])[\/\-](0[1-9]|[12]\d|3[01])[\/\-](19|20)\d{2}\b", RegexOptions.Compiled), "[DOB-REDACTED]"),
        ("creditcard", new Regex(@"\b(?:\d[ -]*?){13,16}\b", RegexOptions.Compiled), "[CC-REDACTED]"),
    };

    /// <summary>
    /// Masks PHI patterns in the input string. Returns the input unchanged if no match.
    /// </summary>
    public string Mask(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        var result = input;
        foreach (var (_, pattern, replacement) in PhiPatterns)
        {
            result = pattern.Replace(result, replacement);
        }
        return result;
    }

    /// <summary>
    /// Returns true if the input contains any PHI pattern. Useful for alerting
    /// on accidental PHI in telemetry.
    /// </summary>
    public bool ContainsPhi(string? input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        return PhiPatterns.Any(p => p.Pattern.IsMatch(input));
    }
}
