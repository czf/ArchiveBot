using Android.OS;
using Czf.Domain.NewsBankWrapper.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ArchiveBot.Maui.App
{
    internal class WebhookBasedLogger : ILogger, ICanLog
    {
        private readonly Uri _webhookEndPoint;
        private readonly HttpClient _httpClient;

        public WebhookBasedLogger(Uri webhookEndPoint, HttpClient httpClient)
        {
            _webhookEndPoint = webhookEndPoint;
            _httpClient = httpClient;
        }

        public class EmptyDisposable : IDisposable
        {
            public void Dispose()
            { }
        }
        public IDisposable BeginScope<TState>(TState state)
        {
            return new EmptyDisposable();
        }

        public void Error(string message)
        {
            _httpClient.PostAsJsonAsync(_webhookEndPoint, new { logLevel = "Error", message = message }).ConfigureAwait(false).GetAwaiter().GetResult().Dispose();
            SendLog(new { logLevel = "Error", message = message });
        }

        public void Info(string message)
        {
            SendLog(new { logLevel = "info", message = message });
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.None:
                    return false;
                case LogLevel.Debug:
                case LogLevel.Information:
                case LogLevel.Warning:
                case LogLevel.Error:
                case LogLevel.Critical:
                    return true;

                default:
                    break;
            }
            return false;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                SendLog (
                    new { logLevel = logLevel.ToString(), message = state?.ToString() , exception = exception});
            }
        }

        private void SendLog(object logPayload)
        {
            try
            {
                _httpClient.PostAsJsonAsync(_webhookEndPoint, logPayload).ConfigureAwait(false).GetAwaiter().GetResult().Dispose();
            }
            catch
            {
                //TODO buffering failed logs
            }
        }
    }
}
