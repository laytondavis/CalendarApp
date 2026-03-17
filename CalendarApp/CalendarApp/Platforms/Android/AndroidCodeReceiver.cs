// Android-only file — only compiled for net9.0-android
#if __ANDROID__
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;

namespace CalendarApp.Platforms.Android;

/// <summary>
/// OAuth code receiver for use with a Web application OAuth client.
/// Google opens in Chrome; after consent Chrome navigates to the HTTPS
/// redirect URI. Android App Links (autoVerify) route that URL directly
/// back to OAuthRedirectActivity without showing a browser chooser,
/// bypassing the Private Network Access block that affects localhost.
///
/// Requires:
///   • Web application OAuth client in Google Cloud Console with
///     https://LaytonsComputers.com in its authorized redirect URIs.
///   • /.well-known/assetlinks.json served from LaytonsComputers.com
///     listing this app's SHA-256 signing certificate (see README for
///     keytool command to obtain the fingerprint).
/// </summary>
public class AndroidCodeReceiver : ICodeReceiver
{
    public const string RedirectUriString =
        "https://LaytonsComputers.com";

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
