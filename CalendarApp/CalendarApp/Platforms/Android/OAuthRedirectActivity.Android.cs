// Android-only file — only compiled for net9.0-android
#if __ANDROID__
using Android.App;
using Android.Content;
using Android.OS;

namespace CalendarApp.Platforms.Android;

/// <summary>
/// Transparent trampoline activity that receives the OAuth redirect URI.
/// Registered in AndroidManifest.xml with an intent-filter for the custom
/// scheme com.companyname.calendarapp://oauth2redirect so that Android
/// routes the browser's redirect back to this activity.
///
/// Immediately passes the full URI to AndroidCodeReceiver and finishes itself,
/// returning the user to whatever was in the foreground before sign-in.
/// </summary>
[Activity(
    Name = "com.companyname.calendarapp.OAuthRedirectActivity",
    Exported = true,
    NoHistory = true,
    LaunchMode = LaunchMode.SingleTop)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "com.companyname.calendarapp",
    DataHost = "oauth2redirect")]
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
