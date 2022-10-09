using Android.App;
using Android.Content;
using Android.Runtime;
using ArchiveBot.Maui.App.Platforms.Android;

namespace ArchiveBot.Maui.App
{
    [Application]
    public class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership)
            : base(handle, ownership)
        {
        }

        //public override void OnCreate()
        //{
        //    base.OnCreate();

        //    var manager = (NotificationManager)GetSystemService(NotificationService);
        //    manager.CreateNotificationChannel(new NotificationChannel("foregroundchannel", "Foreground Channel", NotificationImportance.Low));
        //    var startServiceIntent = new Intent(this, typeof(BotRunnerService));
        //    startServiceIntent.SetAction(Constants.ACTION_START_SERVICE);

        //    StartService(startServiceIntent);
        //}



        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}