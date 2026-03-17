// Android-only file — only compiled for net9.0-android
#if __ANDROID__
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace CalendarApp.Platforms.Android;

/// <summary>
/// Custom OAuth 2.0 code receiver for Android.
///
/// Chrome (and other Android browsers) block redirects from the HTTPS Google
/// OAuth consent page to http://localhost or http://127.0.0.1 via Chrome's
/// Private Network Access policy. The loopback approach that works fine on
/// desktop simply never delivers the authorization code on Android.
///
/// This receiver uses a custom URI scheme instead:
///   com.companyname.calendarapp://oauth2redirect
///
/// The flow is:
///   1. This receiver opens the browser with the Google auth URL.
///   2. After the user consents, Google redirects to the custom URI.
///   3. Android routes that URI to OAuthRedirectActivity (registered in the
///      manifest with a matching intent-filter).
///   4. OAuthRedirectActivity calls AndroidCodeReceiver.Complete() which
///      resolves the TaskCompletionSource and unblocks AuthorizeAsync.
///
/// IMPORTANT: You must add this redirect URI in Google Cloud Console →
///   APIs & Services → Credentials → your Desktop app client →
///   Authorized redirect URIs:  com.companyname.calendarapp://oauth2redirect
/// </summary>
public class AndroidCodeReceiver : ICodeReceiver
{
    // Scheme must match the intent-filter in AndroidManifest.xml
    public const string RedirectUriString = "com.companyname.calendarapp://oauth2redirect";

    /// <inheritdoc />
    public string RedirectUri => RedirectUriString;

    // Static TCS so OAuthRedirectActivity can deliver the result from a
    // different call stack without needing a direct reference to this instance.
    private static TaskCompletionSource<AuthorizationCodeResponseUrl>? _pendingTcs;

    /// <inheritdoc />
    public Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
        AuthorizationCodeRequestUrl url,
        CancellationToken taskCancellationToken)
    {
        var tcs = new TaskCompletionSource<AuthorizationCodeResponseUrl>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingTcs = tcs;

        taskCancellationToken.Register(() =>
            tcs.TrySetCanceled(taskCancellationToken));

        var authUrl = url.Build().AbsoluteUri;
        Console.WriteLine(
            $"[CalendarApp] AndroidCodeReceiver: opening browser for OAuth → " +
            $"{authUrl[..Math.Min(80, authUrl.Length)]}…");

        var intent = new global::Android.Content.Intent(
            global::Android.Content.Intent.ActionView,
            global::Android.Net.Uri.Parse(authUrl));
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);

        return tcs.Task;
    }

    /// <summary>
    /// Called by OAuthRedirectActivity when the custom-scheme redirect arrives.
    /// Parses the full redirect URI and fills an AuthorizationCodeResponseUrl,
    /// then resolves the pending TaskCompletionSource.
    /// </summary>
    public static void Complete(string redirectUri)
    {
        Console.WriteLine($"[CalendarApp] AndroidCodeReceiver.Complete: {redirectUri}");
        var response = ParseRedirectUri(redirectUri);
        _pendingTcs?.TrySetResult(response);
        _pendingTcs = null;
    }

    private static AuthorizationCodeResponseUrl ParseRedirectUri(string redirectUri)
    {
        var response = new AuthorizationCodeResponseUrl();
        try
        {
            var uri = new Uri(redirectUri);
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2) continue;
                var key   = Uri.UnescapeDataString(kv[0]);
                var value = Uri.UnescapeDataString(kv[1]);
                switch (key)
                {
                    case "code":              response.Code             = value; break;
                    case "state":             response.State            = value; break;
                    case "error":             response.Error            = value; break;
                    case "error_description": response.ErrorDescription = value; break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CalendarApp] Error parsing OAuth redirect URI: {ex.Message}");
            response.Error = "parse_error";
        }
        return response;
    }
}
#endif
