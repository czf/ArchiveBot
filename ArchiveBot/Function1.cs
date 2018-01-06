using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using RedditSharp;
using RedditSharp.Things;
using DefaultWebAgent = RedditSharp.WebAgent;
using WaybackMachineWrapper;
using System.Threading.Tasks;

namespace ArchiveBot
{
	public static class Function1
	{
		[FunctionName("ArchiveBot")]
		public static async Task Run([TimerTrigger("0 */10 * * * *")]TimerInfo myTimer, TraceWriter log)
		{
			string user = Environment.GetEnvironmentVariable("BotName");
			string pass = Environment.GetEnvironmentVariable("BotPass");
			string secret = Environment.GetEnvironmentVariable("BotSecret");
			string clientId = Environment.GetEnvironmentVariable("botClientId");
			BotWebAgent agent = new BotWebAgent(user, pass, clientId, secret, "https://www.reddit.com/user/somekindofbot0000/");

			Reddit r = new Reddit(agent, true);

			Listing<Post> posts = r.AdvancedSearch(x => x.Subreddit == "SeattleWA" &&
			(
			//x.Site == "seattleweekly.com" ||
			//x.Site == "geekwire.com" ||
			//x.Site == "twitter.com" ||
			//x.Site == "seattlepi.com" ||
			x.Site == "seattletimes.com"), Sorting.New, TimeSorting.Day);
			
			using (WaybackClient waybackMachine = new WaybackClient())
			{
				foreach (Post p in posts.TakeWhile(x => !x.IsHidden))
				{
					await ProcessPost(p, waybackMachine, log);
				}
                
			}
			log.Info($"C# Timer trigger function executed at: {DateTime.Now}");
		}

        private static async Task ProcessPost(Post p, WaybackClient waybackClient, TraceWriter log)
        {
            try
            {
                p.Hide();
                Uri archivedUrl = null;
                log.Info(p.Url.ToString());
                Uri target = new Uri(p.Url.GetComponents(UriComponents.Host | UriComponents.Path | UriComponents.Scheme, UriFormat.SafeUnescaped));
                AvailableResponse response = await waybackClient.AvailableAsync(target);
                if (response?.archived_snapshots?.closest?.url != null)
                {
                    archivedUrl = response.archived_snapshots.closest.url;
                }
                else
                {
                    archivedUrl = await waybackClient.SaveAsync(target);
                }

                string msg =
    $@"[Archive.org version.]({archivedUrl.ToString()})

----
^^I'm ^^a ^^bot, ^^beep ^^boop";

                log.Info(msg);
                p.Comment(msg);
            }
            catch(Exception e)
            {
                log.Error("", e);
                throw;
            }
		}
	}
}
