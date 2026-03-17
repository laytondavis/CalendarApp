// Android-only file — only compiled for net9.0-android
#if __ANDROID__
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2;

namespace CalendarApp.Platforms.Android;

/// <summary>
/// OAuth 2.0 code receiver for Android that shows Google's sign-in page
/// in an in-app WebView rather than launching Chrome.
///
/// WHY THIS APPROACH:
/// Chrome blocks http://localhost redirects that originate from HTTPS pages
/// (Chrome's Private Network Access policy). This means the standard
/// LoopbackCodeReceiver never receives the authorization code when Chrome
/// handles the OAuth flow.
///
/// The WebView runs inside the app's process, so when Google redirects to
/// http://127.0.0.1:5150/oauth2/callback the WebViewClient intercepts it
/// before any network request is made — the blocking policy never applies.
///
/// Desktop OAuth clients (used here) automatically allow any localhost port
/// as a redirect URI, so no changes are needed in Google Cloud Console.
/// </summary>
public class AndroidWebViewCodeReceiver : ICodeReceiver
{
    // Fixed port on 127.0.0.1 — Desktop app clients allow any localhost port
    // automatically, so this does not need to be registered in Google Console.
    internal const string RedirectUriString = "http://127.0.0.1:5150/oauth2/callback";

    /// <inheritdoc />
    public string RedirectUri => RedirectUriString;

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
            $"[CalendarApp] AndroidWebViewCodeReceiver: opening WebView → " +
            $"{authUrl[..Math.Min(80, authUrl.Length)]}…");

        var intent = new global::Android.Content.Intent(
            global::Android.App.Application.Context,
            typeof(OAuthWebViewActivity));
        intent.PutExtra("auth_url", authUrl);
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);

        return tcs.Task;
    }

    /// <summary>
    /// Called by OAuthWebViewActivity when the WebViewClient intercepts the
    /// redirect to the fixed callback URI. Parses code/state/error from the
    /// query string and resolves the pending task.
    /// </summary>
    public static void Complete(string redirectUri)
    {
        Console.WriteLine($"[CalendarApp] AndroidWebViewCodeReceiver.Complete: {redirectUri}");
        _pendingTcs?.TrySetResult(ParseRedirectUri(redirectUri));
        _pendingTcs = null;
    }

    internal static AuthorizationCodeResponseUrl ParseRedirectUri(string redirectUri)
    {
        var response = new AuthorizationCodeResponseUrl();
        try
        {
            var uri   = new Uri(redirectUri);
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
            Console.WriteLine($"[CalendarApp] Error parsing OAuth redirect: {ex.Message}");
            response.Error = "parse_error";
        }
        return response;
    }
}
#endif
