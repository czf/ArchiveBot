using ArchiveBot.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using System.Windows.Input;

namespace ArchiveBot.Maui.App.Models
{
    public class RunBotActionsViewModel : INotifyPropertyChanged
    {
        private readonly Core.ArchiveBot _archiveBot;
        private readonly EditForNewsbank _editForNewsbank;
        private readonly ILogger _logger;
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private string _serializedException = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;
        public ICommand ClearExceptionCommand { get; private set; }
        public ICommand ForceExceptionCommand { get; private set; }
        public ICommand ExecuteArchiveBotCommand { get; private set; }
        public ICommand ExecuteNewsbankCommand { get; private set; }


        public string SerializedException
        {
            get => _serializedException;
            set
            {
                _serializedException = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowExceptionStuff));
            }
        }
        public bool ShowExceptionStuff => SerializedException != string.Empty;


        public RunBotActionsViewModel(Core.ArchiveBot archiveBot,
        EditForNewsbank editForNewsbank,
        ILogger logger)
        {
            _archiveBot = archiveBot;
            _editForNewsbank = editForNewsbank;
            _logger = logger;
            _jsonSerializerOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,

            };

            ClearExceptionCommand = new Command(() => SerializedException = string.Empty);
            ForceExceptionCommand = new Command<IWebView>(ForceException);
            ExecuteArchiveBotCommand = new Command<IWebView>(ExecuteArchiveBot);
            ExecuteNewsbankCommand= new Command<IWebView>(ExecuteNewsbank);
        }


        public void OnPropertyChanged([CallerMemberName] string name = "") =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));



        private async void ExecuteArchiveBot(IWebView exceptionDisplay)
        {
            try
            {
                await _archiveBot.RunAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "archivebot error");
                await PopulateExceptionDisplay(exception, exceptionDisplay);
            }

        }

        private async void ExecuteNewsbank(IWebView exceptionDisplay)
        {
            try
            {
                await _editForNewsbank.RunAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "editfornewsbank error");
                await PopulateExceptionDisplay(exception, exceptionDisplay);
            }

        }


        private async void ForceException(IWebView exceptionDisplay)
        {
            try
            {
                throw new Exception("some kind of exception");
            }
            catch (Exception exception)
            {
                await PopulateExceptionDisplay(exception, exceptionDisplay).ConfigureAwait(false);
            }
        }

        private async Task PopulateExceptionDisplay(Exception exception, IWebView exceptionDisplay)
        {
            //JsonSerializer can't serialize exception https://github.com/dotnet/runtime/issues/43026
            var exceptionDto = new
            {
                exception.Message,
                exception.InnerException,
                exception.StackTrace,
                exception.Source,
            };
            SerializedException = HttpUtility.JavaScriptStringEncode(JsonSerializer.Serialize(exceptionDto, _jsonSerializerOptions));

            await exceptionDisplay.EvaluateJavaScriptAsync($"populateException(\"{SerializedException}\")").ConfigureAwait(false);
        }
        
    }
}
