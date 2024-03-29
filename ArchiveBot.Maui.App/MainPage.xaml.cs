﻿using Android.App;
using Android.Content;
using Azure.Security.KeyVault.Secrets;

namespace ArchiveBot.Maui.App
{
    public partial class MainPage : ContentPage
    {
        
        int count = 0;

        public MainPage()
        {
            //_secretClient = secretClient;
            InitializeComponent();
            Routing.RegisterRoute("AzureAuthPage", typeof(AzureAuthPage));
        }


        private async void OnCounterClicked(object sender, EventArgs e)
        {
            //var sc = MauiApplication.Current.Services.GetRequiredService<SecretClient>();
            //var name = (await sc.GetSecretAsync("BotName")).Value.Value;

            //await Shell.Current.GoToAsync("AzureAuthPage");
            //try
            //{
            //    var _ = await _secretClient.GetSecretAsync("EZProxyAccount");
            //}
            //catch(Exception ex)
            //{
            //    Console.WriteLine(ex);
            //}
            count++;

            if (count == 1)
                CounterBtn.Text = $"Clicked {count} time";
            else
                CounterBtn.Text = $"Clicked {count} times";
            var d =(AlarmManager)Android.App.Application.Context.GetSystemService(Context.AlarmService);
            SemanticScreenReader.Announce(CounterBtn.Text);
        }

        private void aSwitch_Toggled(object sender, ToggledEventArgs e)
        {

        }
    }
}