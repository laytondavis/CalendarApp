// Android-only file — only compiled for net9.0-android
#if __ANDROID__
using Android.App;
using Android.OS;

namespace CalendarApp.Platforms.Android;

/// <summary>
/// Trampoline activity for the custom-scheme OAuth redirect.
/// Only active when credentials_android.json (Web application client) is
/// present and AndroidCodeReceiver is in use. The intent-filter in
/// AndroidManifest.xml routes com.companyname.calendarapp://oauth2redirect
/// here; this activity passes the URI to AndroidCodeReceiver and finishes.
/// </summary>
[Activity(
    Name = "com.companyname.calendarapp.OAuthRedirectActivity",
    Exported = true,
    NoHistory = true)]
public class OAuthRedirectActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var redirectUri = Intent?.Data?.ToString() ?? string.Empty;
        Console.WriteLine($"[CalendarApp] OAuthRedirectActivity: {redirectUri}");
        AndroidCodeReceiver.Complete(redirectUri);
        Finish();
    }
}
#endif
