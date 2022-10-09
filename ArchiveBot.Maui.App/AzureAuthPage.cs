namespace ArchiveBot.Maui.App;


public class AzureAuthPage : ContentPage, IQueryAttributable
{

	public AzureAuthPage()
	{
		Content = new WebView
		{
			StyleId = "authView"
		};
	}

	public void ApplyQueryAttributes(IDictionary<string, object> query)
	{
		WebView v = (WebView)Content;
		v.Source = query["source"].ToString();
	}
}