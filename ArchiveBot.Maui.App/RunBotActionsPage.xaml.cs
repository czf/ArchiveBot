using ArchiveBot.Core;
using ArchiveBot.Maui.App.Models;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text.Json;
using System.Web;

namespace ArchiveBot.Maui.App;

public partial class RunBotActionsPage : ContentPage
{
    private readonly Core.ArchiveBot _archiveBot;
    private readonly EditForNewsbank _editForNewsbank;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly RunBotActionsViewModel _runBotActionsViewModel;
    
    
    public RunBotActionsPage(
        Core.ArchiveBot archiveBot,
        EditForNewsbank editForNewsbank,
        ILogger logger)
	{
		InitializeComponent();
        BindingContext = 
            _runBotActionsViewModel = new RunBotActionsViewModel(archiveBot, editForNewsbank, logger);
        _archiveBot = archiveBot;
        _editForNewsbank = editForNewsbank;
        _logger = logger;
        _jsonSerializerOptions = new JsonSerializerOptions()
        {
            WriteIndented = true,
            
        };        

#if !DEBUG
        populateeditor.IsVisible = false;
#endif

    }


}