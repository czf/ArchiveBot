using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using RedditSharp;
using RedditSharp.Things;
using WaybackMachineWrapper;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System.Security.Authentication;

namespace ArchiveBot
{
    public static class Function1
    {
        private static bool? _debug;
        public static bool Debug
        { 
            get
            {
                if (!_debug.HasValue)
                {
                    _debug = Convert.ToBoolean(Environment.GetEnvironmentVariable("Debug"));
                }
                return _debug.Value;
            }
        }

        [FunctionName("ArchiveBot")]
        public static async Task Run([TimerTrigger("0 */3 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            string storage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            string user = Environment.GetEnvironmentVariable("BotName");
            string pass = Environment.GetEnvironmentVariable("BotPass");
            string secret = Environment.GetEnvironmentVariable("BotSecret");
            string clientId = Environment.GetEnvironmentVariable("botClientId");

            CloudTable oauthTable = CloudStorageAccount
                .Parse(storage)
                .CreateCloudTableClient()
                .GetTableReference("oauth");

            RedditOAuth result = (RedditOAuth)oauthTable
                .Execute(
                    TableOperation.Retrieve<RedditOAuth>("reddit", user)
                ).Result;
            //https://blog.maartenballiauw.be/post/2012/10/08/what-partitionkey-and-rowkey-are-for-in-windows-azure-table-storage.html
            //https://www.red-gate.com/simple-talk/cloud/cloud-data/an-introduction-to-windows-azure-table-storage/
            


            Reddit r = null;
            BotWebAgent agent = null;
            bool tryLogin = false;
            int tryLoginAttempts = 2;
            do
            {
                tryLoginAttempts--;
                tryLogin = false;
                if (result == null)
                {
                    agent = new BotWebAgent(user, pass, clientId, secret, "https://www.reddit.com/user/somekindofbot0000/");
                    result = new RedditOAuth() { Token = agent.AccessToken, PartitionKey = "reddit", RowKey = user };
                    r = new Reddit(agent, true);
                }
                else
                {
                    try
                    {
                        //agent = new BotWebAgent(result.RefreshToken, clientId, secret, "https://www.reddit.com/user/somekindofbot0000/");
                        r = new Reddit(result.Token);
                    }
                    catch (AuthenticationException a)
                    {
                        result = null;
                        tryLogin = true;
                    }
                    catch(WebException w)
                    {
                        if (w.Status == WebExceptionStatus.ProtocolError
                            && (w.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            result = null;
                            tryLogin = true;
                        }
                        else
                        {
                            throw;
                        }
                    }
                   
                    
                }
                
            } while (tryLogin && tryLoginAttempts > 0);

            if (r == null)
                throw new Exception("couldn't get logged in");


            oauthTable
                .Execute(
                    TableOperation.InsertOrReplace(result));

            CheckMail(r);

            Listing<Post> posts = 
                r.AdvancedSearch(x => x.Subreddit == "SeattleWA" &&
                x.Site == "seattletimes.com"
            , Sorting.New, TimeSorting.Day);
            
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
                if (!Debug)
                {
                    p.Hide();
                }
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


                if (!Debug)
                {
                    p.Comment(msg);
                }
            }
            catch(Exception e)
            {
                log.Error("", e);
                throw;
            }
        }



        private static void CheckMail(Reddit r)
        {
            if (r.User.HasMail)
            {
                string mailBaseAddress = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                using (HttpClient client = new HttpClient())
                {
                    client.BaseAddress = new Uri("https://"+mailBaseAddress);
                    if (!Debug)
                    {
                        client.PostAsync($"api/CheckBotMail/name/{r.User.Name}/",null);
                    }
                }
            }
        }
    }

    public class RedditOAuth : TableEntity
    {
        public string Token { get; set; }
    }
}
