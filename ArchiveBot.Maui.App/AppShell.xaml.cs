namespace ArchiveBot.Maui.App
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute("AzureAuthPage", typeof(AzureAuthPage));
        }

        //protected override void OnAppearing()
        //{
        //    base.OnAppearing();
        //    //MessagingCenter.sub((object)null, "");
        //}
    }
}