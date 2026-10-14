namespace AudiobookDownloader.WinForms;

/// <summary>
/// Recognises the reply Amazon sends the browser to when a sign-in succeeds, and
/// turns it into the one address the Audible library accepts.
/// </summary>
/// <remarks>
/// The whole automatic sign-in rests on this rule — the window must close exactly
/// when the reply arrives, and never before — so it lives on its own and the
/// self-test checks every address it has to accept and refuse, including the
/// addresses the library itself asks Amazon to return to, region by region.
///
/// A reply is judged on two things only: it comes from Amazon's or Audible's own
/// store address, and it carries a non-empty authorization code. The landing path
/// is deliberately *not* required. Amazon's <c>openid.return_to</c> points at
/// <c>/ap/maplanding</c> today, but it is the code that signs the account holder
/// in, and a window that insisted on one particular path would strand the user on
/// a web page the day Amazon moved the page. The library takes the same view: it
/// reads the code out of the query and ignores the rest of the address.
/// </remarks>
internal static class AudibleSignInRedirect
{
    /// <summary>The parameter that carries the authorization code.</summary>
    public const string AuthorizationCodeParameter = "openid.oa2.authorization_code";

    /// <summary>The landing page Amazon returns to today, used to build the reply address.</summary>
    private const string MapLandingPath = "/ap/maplanding";

    /// <summary>
    /// The addresses that count as the store: every Amazon or Audible address the
    /// Audible library knows about, taken from the library's own list of regions
    /// so a region it adds is accepted here without the app having to be changed.
    /// </summary>
    /// <remarks>
    /// Spelled out as whole addresses rather than a suffix test on purpose. A
    /// "host ends with amazon.com" test would accept <c>www.amazon.com.evil.com</c>,
    /// and this address decides whether the app shows the user a signed-in account.
    /// </remarks>
    private static readonly HashSet<string> StoreAddresses = BuildStoreAddresses();

    private static HashSet<string> BuildStoreAddresses()
    {
        var addresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var locale in AudibleApi.Localization.Locales)
        {
            if (string.IsNullOrWhiteSpace(locale.TopDomain))
                continue;

            foreach (var store in new[] { "amazon", "audible" })
            {
                addresses.Add($"{store}.{locale.TopDomain}");
                addresses.Add($"www.{store}.{locale.TopDomain}");
            }
        }

        return addresses;
    }

    /// <summary>True when this address carries an Amazon authorization code.</summary>
    public static bool CarriesAuthorizationCode(string? address) => AuthorizationCode(address) is not null;

    /// <summary>
    /// The authorization code this address carries, or null when it carries none.
    /// Read from the query, and from the fragment as well: Amazon has been seen
    /// sending the parameters after the hash, where a query-only reader would miss
    /// them and leave the account holder stuck on the page.
    /// </summary>
    public static string? AuthorizationCode(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return null;

        if (!StoreAddresses.Contains(uri.Host.TrimEnd('.')))
            return null;

        return Parameter(uri.Query, AuthorizationCodeParameter)
            ?? Parameter(uri.Fragment, AuthorizationCodeParameter);
    }

    /// <summary>
    /// The reply address to hand the Audible library for a code read out of
    /// <paramref name="address"/>.
    /// </summary>
    /// <remarks>
    /// A fresh address is built around the code instead of passing on the one
    /// Amazon used, so whatever else Amazon appended — a fragment, a second code,
    /// a test marker — cannot reach a parser that only wants the query. This is
    /// also what lets a code that arrived in the fragment be handed over at all,
    /// because the library reads the query and nothing else.
    /// </remarks>
    public static string ResponseUrl(string address, string code)
    {
        var origin = Uri.TryCreate(address, UriKind.Absolute, out var uri)
            ? $"{uri.Scheme}://{uri.Authority}"
            : "https://www.amazon.com";

        return $"{origin}{MapLandingPath}?{AuthorizationCodeParameter}={Uri.EscapeDataString(code)}";
    }

    /// <summary>The value of one parameter in a query or fragment, or null when it is absent or blank.</summary>
    private static string? Parameter(string? portion, string name)
    {
        if (string.IsNullOrEmpty(portion))
            return null;

        foreach (var pair in portion.TrimStart('?', '#').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2 || !Decode(parts[0]).Equals(name, StringComparison.Ordinal))
                continue;

            var value = Decode(parts[1]);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>Decodes one parameter, treating "+" as a space exactly as the library's parser does.</summary>
    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            // A malformed escape is not a code; compare it as written and move on.
            return value;
        }
    }
}
