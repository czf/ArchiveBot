using ArchiveBot.Core;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Web;

namespace ArchiveBot.Maui.App;

public partial class RunBotActionsPage : ContentPage
{
    private readonly Core.ArchiveBot _archiveBot;
    private readonly EditForNewsbank _editForNewsbank;
    private readonly ILogger _logger;
    
    public RunBotActionsPage(
        Core.ArchiveBot archiveBot,
        EditForNewsbank editForNewsbank,
        ILogger logger)
	{
		InitializeComponent();
        _archiveBot = archiveBot;
        _editForNewsbank = editForNewsbank;
        _logger = logger;
        ExceptionDisplay.IsVisible = false;
        clearExceptionDisplay.IsVisible = false;
#if !DEBUG
        populateeditor.IsVisible = false;
#endif

    }

    private async void ExecuteArchiveBot_Clicked(object sender, EventArgs e)
    {
        try
        {   
            await _archiveBot.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception) 
        {
            _logger.LogError(exception, "archivebot error");
            PopulateExceptionDisplay(exception);
        }

    }

    private async void ExecuteNewsbank_Clicked(object sender, EventArgs e)
    {
        try
        {
            await _editForNewsbank.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception) 
        {
            _logger.LogError(exception, "editfornewsbank error");
            PopulateExceptionDisplay(exception);
        }

    }

    private void populateeditor_Clicked(object sender, EventArgs e)
    {
        try
        {
            throw new Exception("some kind of exception");
        }
        catch(Exception exception)
        {
            PopulateExceptionDisplay(exception);
        }
    }

    private void clearExceptionDisplay_Clicked(object sender, EventArgs e)
    {
        ExceptionDisplay.EvaluateJavaScriptAsync($"populateException(\"\")");
        ExceptionDisplay.IsVisible = false;
    }

    private void PopulateExceptionDisplay(Exception exception)
    {
        var exceptionString = HttpUtility.JavaScriptStringEncode(JsonConvert.SerializeObject(exception, Formatting.Indented));
        clearExceptionDisplay.IsVisible= true;
        ExceptionDisplay.EvaluateJavaScriptAsync($"populateException(\"{exceptionString}\")");
        ExceptionDisplay.IsVisible = true;
    }
}