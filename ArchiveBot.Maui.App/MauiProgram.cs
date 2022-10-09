using Android.Widget;
using ArchiveBot.Core.Maui;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using ArchiveBot.Maui.App.Platforms.Android;
using Azure.Data.Tables;
using ArchiveBot.Core;
using WaybackMachineWrapper;
using Czf.Api.NewsBankWrapper;
using Microsoft.Extensions.Logging;
using Czf.Domain.NewsBankWrapper.Interfaces;
using ArchiveBot.Core.Objects;

namespace ArchiveBot.Maui.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                .Services
                .AddSingleton<BasicLog>()
                .AddSingleton<ICanLog, BasicLog>()
                .AddSingleton<ILogger, BasicLog>()
                .AddSingleton((services) =>
                {
                    DeviceCodeCredentialOptions options = new DeviceCodeCredentialOptions()
                    {
                        TenantId = "4e72a007-c1d8-4b0f-8fb6-5137abe83221",
                        ClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46",
                        DeviceCodeCallback = async (info, cToken) =>
                        {
                            Dictionary<string, object> uriParams = new() { { "source", info.VerificationUri.AbsoluteUri } };
                            SemaphoreSlim toastSync = new SemaphoreSlim(0, 1);
                            await Clipboard.Default.SetTextAsync(info.UserCode).ConfigureAwait(false);
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                var t = Toast.MakeText(Android.App.Application.Context, info.Message, ToastLength.Long);
                                t.SetGravity(Android.Views.GravityFlags.Fill, 0, 0);
                                t.Show();

                                //MessagingCenter.Send((object)null, "");

                                //Platform.CurrentActivity.SetContentView(view);
                                toastSync.Release();
                            });
                            await toastSync.WaitAsync().ConfigureAwait(false);
                            await Clipboard.Default.SetTextAsync(info.UserCode).ConfigureAwait(false);
                            await MainThread.InvokeOnMainThreadAsync(async () =>
                            {
                                await Shell.Current.GoToAsync("AzureAuthPage", uriParams).ConfigureAwait(false);
                            });
                            //}, "cfa8b339-82a2-471a-a3c9-0fc0be7a4093");
                        }
                    };
                    DeviceCodeCredential c = new DeviceCodeCredential(options);
                    return c;
                })
                .AddSingleton<MainPage>()
                .AddSingleton<IEZProxySignInCredentialsProvider, SecretClientEZProxySignInCredentialProvider>()
                .AddSingleton<IEZProxySignInUriProvider, SecretClientEZProxySignInUriProvider>()
                .AddSingleton<IBotParameterCredentialsProvider, SecretClientBotParameterCredentialsProvider>()
                .AddSingleton<IProductBaseUriProvider, SecretClientProductBaseUriProvider >()
                .AddSingleton((services) =>
                {
                    return new SecretClient(new Uri("https://czfkeyvault.vault.azure.net/"), services.GetService<DeviceCodeCredential>());
                })
                .AddSingleton((services) =>
                {
                    return new TableServiceClient(new Uri("https://czfapp9632.table.core.windows.net/"), services.GetService<DeviceCodeCredential>());
                })
                .AddSingleton<Core.ArchiveBot>()
                .AddSingleton<BotRunnerService>()
                .AddSingleton<EditForNewsbank>()
                .AddSingleton<WaybackClient>()
                .AddSingleton<NewsBankClient>()
                .AddSingleton<CheckBotMail>()
                ;
                
                builder.Services.AddHttpClient<BotRunnerService>((client) =>
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/104.0.5112.102 Safari/537.36");
                    client.DefaultRequestHeaders.Add("Connection", "keep-alive");
                    client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
                    client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");
                    client.BaseAddress = new Uri("https://www.reddit.com/");
                });
                
            return builder.Build();
        }

        public class BasicLog : ILogger , ICanLog
        {
            public class EmptyDisposable : IDisposable
            {
                public void Dispose()
                {}
            }
            public IDisposable BeginScope<TState>(TState state)
            {
                return new EmptyDisposable();
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
                if (IsEnabled(logLevel) && state is string)
                {

                    Console.WriteLine(state.ToString());
                }
            }

            public void Error(string message)
            {
                Console.WriteLine(message);
            }

            public void Info(string message)
            {
                Console.WriteLine(message);
            }
        }
    }
}