using ArchiveBot.Core;
using Microsoft.Extensions.Logging;

namespace ArchiveBot.Maui.App;

public partial class RunBotActionsPage : ContentPage
{
    private readonly Core.ArchiveBot _archiveBot;
    private readonly EditForNewsbank _editForNewsbank;
    private readonly ILogger _logger;
    private string _actionResult;
    public RunBotActionsPage(
        Core.ArchiveBot archiveBot,
        EditForNewsbank editForNewsbank,
        ILogger logger)
	{
		InitializeComponent();
        _archiveBot = archiveBot;
        _editForNewsbank = editForNewsbank;
        _logger = logger;

    }

    private async void ExecuteArchiveBot_Clicked(object sender, EventArgs e)
    {
        try
        {
            BotActionResultEditor.Text = string.Empty;
            await _archiveBot.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception) 
        {
            _logger.LogError(exception, "archivebot error");
            BotActionResultEditor.Text = "archivebot error \n\n" + exception.ToString();
        }

    }

    private async void ExecuteNewsbank_Clicked(object sender, EventArgs e)
    {
        try
        {
            BotActionResultEditor.Text = string.Empty;
            await _editForNewsbank.RunAsync().ConfigureAwait(false);
        }
        catch (Exception exception) 
        {
            _logger.LogError(exception, "editfornewsbank error");
            BotActionResultEditor.Text = "editfornewbank error \n\n" + exception.ToString();

        }

    }

    private void populateeditor_Clicked(object sender, EventArgs e)
    {
        try
        {
            BotActionResultEditor.Text = string.Empty;

            throw new Exception("some kind of exception");
        }
        catch(Exception exception)
        {
            BotActionResultEditor.Text = "asdfasdfsafadfdsafds error \n\n" + exception.ToString();

        }

    }
}