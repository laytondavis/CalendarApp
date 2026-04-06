// Android-only file — only compiled for net9.0-android
#if __ANDROID__
using Android.App;
using Android.OS;
using Android.Webkit;

namespace CalendarApp.Platforms.Android;

/// <summary>
/// Full-screen Activity that hosts Google's OAuth consent page in a WebView.
/// Used instead of launching Chrome so that the redirect to http://127.0.0.1
/// is intercepted inside the app before Chrome's Private Network Access
/// blocking policy can reject it.
///
/// The Activity is started by AndroidWebViewCodeReceiver and finishes itself
/// as soon as the WebViewClient intercepts the redirect URI, returning the
/// user to the CalendarApp.
/// </summary>
[Activity(
    Name = "com.companyname.calendarapp.OAuthWebViewActivity",
    Exported = false)]
public class OAuthWebViewActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var authUrl = Intent?.GetStringExtra("auth_url") ?? string.Empty;
        if (string.IsNullOrEmpty(authUrl))
        {
            AndroidWebViewCodeReceiver.Complete(string.Empty);
            Finish();
            return;
        }

        var webView = new WebView(this);
        webView.Settings.JavaScriptEnabled  = true;
        webView.Settings.DomStorageEnabled  = true;

        // Use a standard mobile Chrome user-agent string. Google's sign-in page
        // detects "wv" (Android WebView) in the default UA and may show a warning
        // or block the login form. Replacing it with a normal Chrome mobile UA
        // ensures the full sign-in flow is shown.
        webView.Settings.UserAgentString =
            "Mozilla/5.0 (Linux; Android 10; Mobile) " +
            "AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/120.0.0.0 Mobile Safari/537.36";

        webView.SetWebViewClient(new OAuthWebViewClient(this));
        webView.LoadUrl(authUrl);

        SetContentView(webView);
    }

    // ── WebViewClient ────────────────────────────────────────────────────────

    private sealed class OAuthWebViewClient : WebViewClient
    {
        private readonly OAuthWebViewActivity _activity;

        public OAuthWebViewClient(OAuthWebViewActivity activity)
            => _activity = activity;

        public override bool ShouldOverrideUrlLoading(
            Android.Webkit.WebView? view, IWebResourceRequest? request)
        {
            var url = request?.Url?.ToString() ?? string.Empty;

            // The Desktop OAuth client allows any localhost port automatically.
            // Intercept the redirect before the WebView attempts the network call.
            if (url.StartsWith(AndroidWebViewCodeReceiver.RedirectUriString,
                               StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[CalendarApp] OAuthWebViewActivity: intercepted redirect → {url}");
                AndroidWebViewCodeReceiver.Complete(url);
                _activity.Finish();
                return true; // we handled it — don't let WebView load the URL
            }

            return false; // let WebView navigate normally (Google sign-in pages)
        }
    }
}
#endif
