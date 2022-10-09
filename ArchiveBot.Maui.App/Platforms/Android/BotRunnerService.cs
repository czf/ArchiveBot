﻿using Android.App;
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
using Exception = System.Exception;

namespace ArchiveBot.Maui.App.Platforms.Android
{
    [Service(Permission = "android.permission.BIND_JOB_SERVICE")]
    public class BotRunnerService : Service
    {
        bool _disposed;
        bool _isStarted;
        Runnable _archiveBotRunnable;
        Runnable _editForNewbankRunnable;
        private Runnable _tempRunnable;
        private Runnable _tempRunnable2;
        private ScheduledThreadPoolExecutor _scheduledThreadPoolExecutor;
        private IScheduledFuture _archiveBotFuture;
        private Core.ArchiveBot _archiveBot;
        private IScheduledFuture _editForNewsbankFuture;
        private EditForNewsbank _editForNewsbank;
        private CheckBotMail _checkBotMail;
        private ILogger _logger;



        public override void OnCreate()
        {
            base.OnCreate();
            try
            {
                _logger = MauiApplication.Current.Services.GetRequiredService<ILogger>();
                _archiveBot = MauiApplication.Current.Services.GetRequiredService<Core.ArchiveBot>();
                _editForNewsbank = MauiApplication.Current.Services.GetRequiredService<EditForNewsbank>();
                _checkBotMail = MauiApplication.Current.Services.GetRequiredService<CheckBotMail>();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                _logger.LogError(e, "error during OnCreate");
                throw;
            }
           
            _archiveBotRunnable = new Runnable(ExecuteArchiveBot);
            _editForNewbankRunnable = new Runnable(ExecuteEditForNewsbank);

            _tempRunnable = new Runnable(ExecuteEditForNewsbank);
            

            _scheduledThreadPoolExecutor = new ScheduledThreadPoolExecutor(1);

            //_handler = new Handler(s);
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            _scheduledThreadPoolExecutor.ShutdownNow();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                _scheduledThreadPoolExecutor.Dispose();
                _archiveBotFuture.Dispose();
                _archiveBotRunnable.Dispose();
                _editForNewbankRunnable.Dispose();
                _editForNewsbankFuture.Dispose();
            }
            _disposed = true;
        }

        public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId)
        {
            base.OnStartCommand(intent, flags, startId);
            if (intent.Action.Equals(Constants.ACTION_START_SERVICE))
            {
                if (!_isStarted)
                {
                    RegisterForegroundService();

                    //_archiveBotFuture = _scheduledThreadPoolExecutor.ScheduleAtFixedRate(_archiveBotRunnable, 0, 10, TimeUnit.Minutes);

                    double millisecondDelay = 0;
                    if (DateTime.Now.Hour < 8)
                    {
                        millisecondDelay = DateTime.Now.Date.AddHours(8).Subtract(DateTime.Now).TotalMilliseconds;
                    }
                    else
                    {
                        millisecondDelay = DateTime.Now.Date.AddDays(1).AddHours(8).Subtract(DateTime.Now).TotalMilliseconds;
                    }
                    _editForNewsbankFuture = _scheduledThreadPoolExecutor.ScheduleAtFixedRate(_editForNewbankRunnable, (long)millisecondDelay, (long)TimeSpan.FromDays(1).TotalMilliseconds, TimeUnit.Milliseconds);

                    _archiveBotFuture = _scheduledThreadPoolExecutor.ScheduleWithFixedDelay(_archiveBotRunnable, 0, (long)TimeSpan.FromMinutes(10).TotalMilliseconds, TimeUnit.Milliseconds);
                    //_archiveBotFuture = _scheduledThreadPoolExecutor.Schedule(_tempRunnable, 3000, TimeUnit.Milliseconds);
                    
                    //_future = _scheduledThreadPoolExecutor.ScheduleAtFixedRate(_runnable, 3000, 3000, TimeUnit.min);
                    _isStarted = true;
                }
            }
            // This tells Android not to restart the service if it is killed to reclaim resources.
            return StartCommandResult.Sticky;
        }

        private async void ExecuteArchiveBot()
        {
            try
            {
                await _archiveBot.RunAsync();
            }
            catch(Exception e)
            {
                _logger.LogError(e, "archivebot error");
            }
        }

        private async void ExecuteEditForNewsbank()
        {
            try
            {
                await _editForNewsbank.RunAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "editForNewsbank error");
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
