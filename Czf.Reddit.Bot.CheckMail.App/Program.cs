using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RedditSharp;
using RedditSharp.Things;

using System.Text;



// See https://aka.ms/new-console-template for more information
Console.WriteLine("Hello, World!");
using IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(x => {
        x.AddUserSecrets<Program>();
        x.AddEnvironmentVariables();
    })
    .ConfigureServices((hostContext, services) =>
    {
        
        services.AddHttpClient();

        List<Bot> bots = new();
        hostContext.Configuration.Bind("bots", bots);
        foreach (var bot in bots)
        {


            services
            .AddSingleton<Reddit>((provider) =>
            {


                string user = bot.RedditUser;
                string pass = bot.Pass;
                string clientId = bot.ClientId;
                string secret = bot.Secret;
                if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret))
                {
                    throw new ArgumentException("missing one or more environment variables");
                }


                var c = provider.GetRequiredService<IHttpClientFactory>().CreateClient();


                Reddit? r = null;
                int count = 0;
                do
                {
                    try
                    {

                        BotWebAgent? agent = new BotWebAgent(user, pass, clientId, secret, $"https://www.reddit.com/user/{user}/", c);
                        r = new Reddit(agent, true);
                    }
                    catch (Exception ex)
                    {
                        count++;
                        Console.WriteLine("failed");
                        Console.WriteLine(ex);
                    }
                }
                while (r == null && count < 10);




                if (r == null)
                    throw new Exception("couldn't get logged in");
                return r;
            });
        }

    }).Build();






var redditBots = host.Services.GetRequiredService<IEnumerable<Reddit>>();
foreach (var redditBot in redditBots)
{
    bool hasMail = await redditBot.User.GetUnreadMessages().AnyAsync();

    if (hasMail)
    {
        throw new Exception("Has mail");
    }
}

public class Bot
{
    public string RedditUser { get; set; } = string.Empty;
    public string Pass { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}
