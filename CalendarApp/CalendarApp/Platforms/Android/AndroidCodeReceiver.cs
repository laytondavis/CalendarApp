// Android-only file — only compiled for net9.0-android
#if __ANDROID__
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace CalendarApp.Platforms.Android;

/// <summary>
/// OAuth code receiver that uses a custom URI scheme redirect.
/// Used when credentials_android.json (Web application OAuth client) is
/// present. Google opens in Chrome; after consent Chrome routes the
/// redirect URI back to OAuthRedirectActivity via Android's intent system,
/// completely bypassing the Private Network Access block that affects
/// http://localhost redirects.
///
/// Requires: the Web application OAuth client in Google Cloud Console must
/// have  com.companyname.calendarapp://oauth2redirect  in its authorized
/// redirect URIs.
/// </summary>
public class AndroidCodeReceiver : ICodeReceiver
{
    public const string RedirectUriString =
        "com.companyname.calendarapp://oauth2redirect";

    public string RedirectUri => RedirectUriString;

    private static TaskCompletionSource<AuthorizationCodeResponseUrl>? _pendingTcs;

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
            $"[CalendarApp] AndroidCodeReceiver: opening browser → " +
            $"{authUrl[..Math.Min(80, authUrl.Length)]}…");

        var intent = new global::Android.Content.Intent(
            global::Android.Content.Intent.ActionView,
            global::Android.Net.Uri.Parse(authUrl));
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(intent);

        return tcs.Task;
    }

    public static void Complete(string redirectUri)
    {
        Console.WriteLine($"[CalendarApp] AndroidCodeReceiver.Complete: {redirectUri}");
        _pendingTcs?.TrySetResult(
            AndroidWebViewCodeReceiver.ParseRedirectUri(redirectUri));
        _pendingTcs = null;
    }
}
#endif
