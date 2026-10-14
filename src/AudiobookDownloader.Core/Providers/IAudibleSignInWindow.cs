using System.Net;

namespace AudiobookDownloader.Core;

/// <summary>
/// Everything the sign-in window needs: the Amazon address to open and the
/// device cookies Amazon must see while the account holder signs in.
/// </summary>
/// <remarks>
/// The cookies carry the device identity the download licence is later issued
/// to. They belong in the browser that shows the page — without them Amazon
/// signs the user in but the authorization code cannot register a device.
/// </remarks>
public sealed record AudibleSignInRequest(string LoginUrl, CookieCollection SignInCookies);

/// <summary>
/// Shows the Amazon sign-in page and hands back the address Amazon redirects to
/// once the account holder has signed in.
/// </summary>
/// <remarks>
/// Implemented by the UI, because the sign-in has to happen in a window the app
/// owns: the redirect that carries the authorization code exists only for a
/// moment, so it must be recognised as it arrives. Nothing is ever typed,
/// copied or pasted by hand.
/// </remarks>
public interface IAudibleSignInWindow
{
    /// <summary>
    /// Opens the sign-in page and blocks until the account holder has finished
    /// or closed the window. Returns the address that carries the authorization
    /// code, or null when the window was closed without signing in.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The window itself could not be used (for example the embedded browser is
    /// not installed); the message is written for the user.
    /// </exception>
    string? ShowSignIn(AudibleSignInRequest request);
}
