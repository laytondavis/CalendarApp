// Android-only file — only compiled for net9.0-android
#if __ANDROID__
using Android.App;
using Android.OS;

namespace CalendarApp.Platforms.Android;

/// <summary>
/// Transparent trampoline activity that receives the Google OAuth redirect URI.
///
/// The intent-filter is declared in AndroidManifest.xml so Android routes any
/// URI matching  com.companyname.calendarapp://oauth2redirect  to this activity.
///
/// Immediately passes the full URI to AndroidCodeReceiver and finishes itself,
/// returning the user to whatever was in the foreground before sign-in.
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
        Console.WriteLine($"[CalendarApp] OAuthRedirectActivity received: {redirectUri}");

        AndroidCodeReceiver.Complete(redirectUri);
        Finish(); // close immediately — user sees the CalendarApp again
    }
}
#endif
