﻿using Czf.Wa.Initiatives;
using Czf.Wa.Initiatives.Dto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RedditSharp;
using RedditSharp.Things;
using System.Net;
using System.Security.Authentication;
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
        services.AddHttpClient<IInitiativeClient>();
        services.AddHttpClient("botwebagent", x =>
        {
            x.Timeout = TimeSpan.FromMinutes(5);            
        });
        services
        .AddSingleton<IInitiativeClient,InitiativeClient>()
        .AddSingleton<Reddit>((provider) =>
        {
            
            string user = hostContext.Configuration["user"] ?? string.Empty;
            string pass = hostContext.Configuration["pass"] ?? string.Empty;
            string clientId = hostContext.Configuration["clientId"] ?? string.Empty;
            string secret = hostContext.Configuration["secret"] ?? string.Empty;
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass)|| string.IsNullOrEmpty(clientId)|| string.IsNullOrEmpty(secret))
            {
                throw new ArgumentException("missing one or more environment variables");
            }


            var c = provider.GetRequiredService<IHttpClientFactory>().CreateClient("botwebagent");
            


            Reddit? r = null;
            int count = 0;
            do
            {
                try
                {

                    BotWebAgent? agent = new BotWebAgent(user, pass, clientId, secret, "https://www.reddit.com/user/somekindofbot0001/", c);
                    r = new Reddit(agent, true);
                }
                catch (Exception ex)
                {
                    count++;
                    Console.WriteLine("failed");
                    Console.WriteLine(ex);
                }
            }
            while (r==null && count<10);




            if (r == null)
                throw new Exception("couldn't get logged in");
            return r;
        });

    }).Build();

var initiativeClient = host.Services.GetRequiredService<IInitiativeClient>();



var reddit = host.Services.GetRequiredService<Reddit>();

var lastPostDate = await GetLastDate(reddit);
Console.WriteLine(lastPostDate);

public partial class Program
{
    const string POST_TITLE_TEMPLATE = "{0} proposed initiative(s) to the {1} submitted since {2}.";
    const string PEOPLE = "people";
    const string LEGISLATURE = "legislature";
    const string TABLE_HEADER_TEMPLATE = "| Subject  | Sponsor    | Assigned Number | Title Letter | Complete Text |";
    const string TABLE_ALIGNMENT_TEMPLATE = "|-|-|-|-|-|";
    const string TABLE_CONTENT_TEMPLATE = "|{0}|{1}|{2}|{3}|{4}|";
    const string ANCHOR_TEMPLATE = "[{0}]({1})";//[link](http://redditpreview.com)
    const string COMMENT_TEMPLATE = "{0}  \n\n{1}";


    public static async Task MakeInitiativePost(Reddit reddit, string postTitle, List<Initiative> initiatives)
    {
        StringBuilder table = new StringBuilder();
        table.AppendLine(TABLE_HEADER_TEMPLATE);
        table.AppendLine(TABLE_ALIGNMENT_TEMPLATE);

        foreach (var init in initiatives.OrderBy(x => x.id))
        {
            init.Deconstruct(out int id, out DateTimeOffset filed, out string fallback, out int assignedNumber, out string sponsor, out string subject, out ContactInfo contactInfo, out string? ballotTitle, out string? ballotSummary, out Uri? ballotTitleLetter, out Uri? completeText, out string? fullText);

            var ballotTitleCell = string.Empty;
            if (ballotTitleLetter != null)
            {
                ballotTitleCell = string.Format(ANCHOR_TEMPLATE, "Ballot Title Letter", ballotTitleLetter.ToString());
            }

            var ballotSummaryCell = string.Empty;
            if (completeText != null)
            {
                ballotSummaryCell = string.Format(ANCHOR_TEMPLATE, "View Complete Text", completeText.ToString());
            }

            if (ballotTitleLetter == null && completeText == null)
            {
                ballotSummaryCell = "initiative submitted as text no docs provided";
            }

            if (sponsor == "Tim Eyman")
            {
                var link = RandomTeLink();
                sponsor = string.Format(ANCHOR_TEMPLATE, sponsor, link.ToString());
            }

            var row = string.Format(TABLE_CONTENT_TEMPLATE,
                subject,
                sponsor + " " + contactInfo.website ?? string.Empty,
                assignedNumber == 0 ? "Not yet assigned" : assignedNumber,
                ballotTitleCell,
                ballotSummaryCell);

            table.AppendLine(row);
        }

        //bot

        await MakeSeattleWeeklyThreadComment(reddit, postTitle, table);
        await MakeWashingtonPost(reddit, postTitle, table);
    }

    private static async Task MakeWashingtonPost(Reddit reddit, string postTitle, StringBuilder table)
    {
        try
        {
            //Seattle
            var weeklyPosts = reddit.Search<Post>("subreddit:seattle flair:\"Weekly+Thread\"", Sorting.New, TimeSorting.Day, 10);
            var post = await weeklyPosts.FirstOrDefaultAsync(x => x.IsSelfPost && x.AuthorName == "AutoModerator");
            if (post != null)
            {
                await post.CommentAsync(string.Format(COMMENT_TEMPLATE, postTitle, table.ToString()));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception in Seattle Weekly Thread");
            Console.WriteLine(ex);
        }
    }

    private static async Task MakeSeattleWeeklyThreadComment(Reddit reddit, string postTitle, StringBuilder table)
    {
        //Washington
        try
        {
            var p = await (await reddit.GetSubredditAsync("washington")).SubmitTextPostAsync(postTitle, table.ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception in Washington");
            Console.WriteLine(ex);
        }
    }

    public static Uri RandomTeLink()
    {
        Uri? result = null;
        switch (new Random().Next(6))
        {
            case 0:
                result = new Uri("https://en.wikipedia.org/wiki/Tim_Eyman");
                break;
            case 1:
                result = new Uri("https://www.permanentdefense.org/timeyman/");
                break;
            case 2:
                result = new Uri("https://ballotpedia.org/Tim_Eyman");
                break;
            case 3:
                result = new Uri("https://www.seattletimes.com/seattle-news/politics/tim-eyman-forced-to-sell-house-to-pay-campaign-finance-fines-debts/");
                break;
            case 4:
                result = new Uri("https://www.atg.wa.gov/news/news-releases/appeals-court-upholds-campaign-finance-ruling-against-tim-eyman");
                break;
            case 5:
                result = new Uri("https://komonews.com/archive/love-him-or-hate-him-but-is-tim-eyman-a-horses-ass");
                break;
        }
        return result!;
    }

    public static async Task<DateTimeOffset> GetLastDate(Reddit reddit)
    {
        
        DateTimeOffset created = new DateTimeOffset(DateTimeOffset.Now.Year, 1, 1, 0, 0, 0, TimeSpan.FromHours(-8));
        var post = await reddit.User.GetPosts(RedditSharp.Things.Sort.New, 1, RedditSharp.Things.FromTime.All).FirstOrDefaultAsync();
        if(post != null)
        {
            var pacificStandarTime = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
            created = new DateTimeOffset(post.CreatedUTC, TimeSpan.Zero);
            created = TimeZoneInfo.ConvertTimeFromUtc(created.DateTime, pacificStandarTime);
        }
        return created;
    }

    public static async Task<List<Initiative>> GetRecentInitiativesToThePeople(DateTimeOffset lastPostDate, IInitiativeClient initiativeClient)
    {
        var allInitiatives = await initiativeClient.GetInitiativeToThePeopleHeaders();
        if(allInitiatives == null)
        {
            allInitiatives = new();
        }
        var result = new List<Initiative>();
        foreach(var initiativeHeader in allInitiatives.Where(x => x.dateFiled > lastPostDate))
        {
            var initiative = await initiativeClient.GetInitiative(initiativeHeader);
            if (initiative != null) 
            {
                result.Add(initiative);
            }
        }
        return result;
    }

    public static string? GeneratePostTitleForThePeople(List<Initiative> peopleInitiatives, DateTimeOffset lastPostDate)
    {
        if(peopleInitiatives.Count == 0)
        {
            return null;
        }
        return String.Format(POST_TITLE_TEMPLATE, peopleInitiatives.Count, PEOPLE, lastPostDate.DateTime.ToShortDateString());

    }

    public static async Task<List<Initiative>> GetRecentInitiativesToTheLegislature(DateTimeOffset lastPostDate, IInitiativeClient initiativeClient)
    {
        var allInitiatives = await initiativeClient.GetInitiativeToTheLegislatureHeaders();
        if(allInitiatives == null)
        {
            allInitiatives = new();
        }
        var result = new List<Initiative>();
        foreach(var initiativeHeader in allInitiatives.Where(x => x.dateFiled > lastPostDate))
        {
            var initiative = await initiativeClient.GetInitiative(initiativeHeader);
            if (initiative != null) 
            {
                result.Add(initiative);
            }
        }
        return result;
    }

    public static string? GeneratePostTitleForTheLegislature(List<Initiative> legislatureInitiatives, DateTimeOffset lastPostDate)
    {
        if(legislatureInitiatives.Count == 0)
        {
            return null;
        }
        return String.Format(POST_TITLE_TEMPLATE, legislatureInitiatives.Count, LEGISLATURE, lastPostDate.DateTime.ToShortDateString());

    }
}
