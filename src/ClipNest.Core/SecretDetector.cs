using System.Text.RegularExpressions;

namespace ClipNest.Core;

/// <summary>
/// Recognizes copied credentials by their shape (private keys, cloud/API tokens, JWTs) so they are not kept
/// in history even when the source app did not mark them as sensitive.
/// </summary>
public static partial class SecretDetector
{
    /// <summary>True when the text is (or contains) something that looks like a secret.</summary>
    public static bool LooksSecret(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20_000) return false;
        return PrivateKey().IsMatch(text) || Token().IsMatch(text) || Jwt().IsMatch(text);
    }

    [GeneratedRegex(@"-----BEGIN (?:RSA |EC |DSA |OPENSSH |PGP |ENCRYPTED )?PRIVATE KEY(?: BLOCK)?-----")]
    private static partial Regex PrivateKey();

    // AWS access key, GitHub, GitLab, Slack, OpenAI/Anthropic-style, Google API, Stripe live keys.
    [GeneratedRegex(@"\b(?:AKIA[0-9A-Z]{16}|gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{60,}|glpat-[A-Za-z0-9_\-]{20,}|xox[abposr]-[A-Za-z0-9-]{10,}|sk-(?:ant-|proj-)?[A-Za-z0-9_\-]{32,}|AIza[0-9A-Za-z_\-]{35}|[sr]k_live_[0-9A-Za-z]{24,})\b")]
    private static partial Regex Token();

    // header.payload.signature where header and payload are base64url JSON starting with {"
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]{10,}\.eyJ[A-Za-z0-9_\-]{10,}\.[A-Za-z0-9_\-]{10,}")]
    private static partial Regex Jwt();
}
