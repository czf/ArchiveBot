using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using ArchiveBot.Core;
using Java.Lang;
using Java.Util.Concurrent;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WaybackMachineWrapper;
using static Android.OS.PowerManager;
using Exception = System.Exception;
using Application = Android.App.Application;
using Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific;

namespace ArchiveBot.Maui.App.Platforms.Android
{
    [Service(Permission = "android.permission.BIND_JOB_SERVICE")]
    public class BotRunnerService : Service
    {
        bool _isStarted;


        private ILogger _logger;
        private WakeLock _wakeLock;


        public override void OnCreate()
        {
            base.OnCreate();
            try
            {
                _logger = MauiApplication.Current.Services.GetRequiredService<ILogger>();
#if !DEBUG
                //PowerManager powerManager = (PowerManager)GetSystemService(PowerService);
                //_wakeLock = powerManager.NewWakeLock(WakeLockFlags.Partial,
                //        "BotRunnerService::WakelockTag");
                //_wakeLock.Acquire();
                
                
#endif
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                _logger.LogError(e, "error during OnCreate");
                throw;
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            _logger.LogWarning("OnDestory Called");
        }


        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            base.OnStartCommand(intent, flags, startId);
            if (intent.Action.Equals(Constants.ACTION_START_SERVICE))
            {
                if (!_isStarted)
                {
                    RegisterForegroundService();
                    double millisecondDelay = EditForNewsBankAlarmReceiver.NextEditForNewsbankDelay();
                    var d = (AlarmManager)Application.Context.GetSystemService(Context.AlarmService);

                    PendingIntent pendingIntent = PendingIntent.GetBroadcast(this, 1,
                        new Intent(this, typeof(ArchiveBotAlarmReceiver)),
                        PendingIntentFlags.UpdateCurrent);
                    d.SetAndAllowWhileIdle(AlarmType.Rtc, DateTimeOffset.UtcNow.AddSeconds(5).ToUnixTimeMilliseconds(), pendingIntent);
                    pendingIntent = PendingIntent.GetBroadcast(this, 1,
                        new Intent(this, typeof(EditForNewsBankAlarmReceiver)),
                        PendingIntentFlags.UpdateCurrent);
                    d.SetAndAllowWhileIdle(AlarmType.Rtc, DateTimeOffset.UtcNow.AddMilliseconds(millisecondDelay).ToUnixTimeMilliseconds(), pendingIntent);
                    _isStarted = true;
                }
            }
            // This tells Android not to restart the service if it is killed to reclaim resources.
            return StartCommandResult.Sticky;
        }

        

        [BroadcastReceiver]
        class ArchiveBotAlarmReceiver : BroadcastReceiver
        {
            private readonly Core.ArchiveBot _archiveBot;
            private readonly ILogger _logger;

            public ArchiveBotAlarmReceiver()
            {
                _archiveBot = MauiApplication.Current.Services.GetRequiredService<Core.ArchiveBot>();
                _logger = MauiApplication.Current.Services.GetRequiredService<ILogger>();
            }

            public override async void OnReceive(Context context, Intent intent)
            {
                try
                {
                    await _archiveBot.RunAsync();
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, "archivebot error");

                }
                finally
                {
                    var d = (AlarmManager)Application.Context.GetSystemService(Context.AlarmService);

                    PendingIntent pendingIntent = PendingIntent.GetBroadcast(Application.Context, 1,
                        new Intent(Application.Context, typeof(ArchiveBotAlarmReceiver)),
                        PendingIntentFlags.UpdateCurrent);
                    d.SetAndAllowWhileIdle(AlarmType.Rtc, DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeMilliseconds(), pendingIntent);
                }
            }
        }
        [BroadcastReceiver]
        class EditForNewsBankAlarmReceiver : BroadcastReceiver
        {
            private readonly EditForNewsbank _editForNewsbank;
            private readonly ILogger _logger;
            public EditForNewsBankAlarmReceiver()
            {
                _editForNewsbank = MauiApplication.Current.Services.GetRequiredService<EditForNewsbank>();
                _logger = MauiApplication.Current.Services.GetRequiredService<ILogger>();
            }

            public override async void OnReceive(Context context, Intent intent)
            {
                bool success = true;
                try
                {
                    await _editForNewsbank.RunAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "editfornewsbank error");
                    success = false;
                }
                finally
                {
                    var d = (AlarmManager)Application.Context.GetSystemService(Context.AlarmService);

                    PendingIntent pendingIntent = PendingIntent.GetBroadcast(Application.Context, 1,
                        new Intent(Application.Context, typeof(EditForNewsBankAlarmReceiver)),
                        PendingIntentFlags.UpdateCurrent);
                    if (success)
                    {
                        d.SetAndAllowWhileIdle(AlarmType.Rtc, DateTimeOffset.UtcNow.AddMilliseconds(NextEditForNewsbankDelay()).ToUnixTimeMilliseconds(), pendingIntent);
                    }
                    else
                    {
                        d.SetAndAllowWhileIdle(AlarmType.Rtc, DateTimeOffset.UtcNow.AddMilliseconds(TimeSpan.FromMinutes(5).TotalMilliseconds).ToUnixTimeMilliseconds(), pendingIntent);
                    }
                }
            }

            public static double NextEditForNewsbankDelay()
            {
                double millisecondDelay = 0;
                if (DateTime.Now.Hour < 8)
                {
                    millisecondDelay = DateTime.Now.Date.AddHours(8).Subtract(DateTime.Now).TotalMilliseconds;
                }
                else
                {
                    millisecondDelay = DateTime.Now.Date.AddDays(1).AddHours(8).Subtract(DateTime.Now).TotalMilliseconds;
                }

                return millisecondDelay;
            }
        }

        void RegisterForegroundService()
        {
            var notification = new Notification.Builder(this, "foregroundchannel")
                .SetContentTitle(Resources.GetString(Resource.String.app_name))
                .SetContentText(Resources.GetString(Resource.String.notification_text))
                //.SetSmallIcon(Resource.Drawable.ic_stat_name)
                //.SetContentIntent(BuildIntentToShowMainActivity())
                .SetOngoing(true)
                //.AddAction(BuildRestartTimerAction())
                //.AddAction(BuildStopServiceAction())
                .Build();


            // Enlist this instance of the service as a foreground service
            StartForeground(Constants.SERVICE_RUNNING_NOTIFICATION_ID, notification);
        }
        public override IBinder OnBind(Intent intent)
        {
            // Return null because this is a pure started service. A hybrid service would return a binder that would
            // allow access to the GetFormattedStamp() method.
            return null;
        }
    }
    public static class Constants
    {
        public const string ACTION_START_SERVICE = "ArchiveBot.Service.action.START_SERVICE";
        public const string ACTION_STOP_SERVICE = "ArchiveBot.Service.action.STOP_SERVICE";
        public const string ACTION_RESTART_TIMER = "ArchiveBot.Service.action.RESTART_TIMER";
        public const string ACTION_MAIN_ACTIVITY = "ArchiveBot.Service.action.MAIN_ACTIVITY";
        public const int SERVICE_RUNNING_NOTIFICATION_ID = 10000;
    }
    
}
