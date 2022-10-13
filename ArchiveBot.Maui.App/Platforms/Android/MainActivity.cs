using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using ArchiveBot.Maui.App.Platforms.Android;

namespace ArchiveBot.Maui.App
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {

        protected override void OnPostCreate(Bundle savedInstanceState)
        {
            base.OnPostCreate(savedInstanceState);
            var manager = (NotificationManager)GetSystemService(NotificationService);
            manager.CreateNotificationChannel(new NotificationChannel("foregroundchannel", "Foreground Channel", NotificationImportance.Low));
#if DEBUG
            var startServiceIntent = new Intent(this, typeof(DebugBotRunnerService));
#else
            var startServiceIntent = new Intent(this, typeof(BotRunnerService));
#endif            
            startServiceIntent.SetAction(Constants.ACTION_START_SERVICE);

            StartService(startServiceIntent);
        }
    }
}