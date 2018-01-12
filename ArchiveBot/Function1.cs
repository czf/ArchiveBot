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
using Microsoft.Azure;
namespace ArchiveBot
{
    public static class Function1
    {
        private static bool _hasRunInit;

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


        private static string _storage;
        private static string _user;
        private static string _pass;
        private static string _secret;
        private static string _clientId;




        [FunctionName("ArchiveBot")]
        public static async Task Run([TimerTrigger("0 */10 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            bool checkTableExists = false;
            if (!_hasRunInit)
            {
                Init();
                checkTableExists = true;
            }


            CloudTable oauthTable = CloudStorageAccount
                .Parse(_storage)
                .CreateCloudTableClient()
                .GetTableReference("oauth");
            if (checkTableExists)
            {
                oauthTable.CreateIfNotExists();
            }



            RedditOAuth result = (RedditOAuth)oauthTable
                .Execute(
                    TableOperation.Retrieve<RedditOAuth>("reddit", _user)
                ).Result;
            //https://blog.maartenballiauw.be/post/2012/10/08/what-partitionkey-and-rowkey-are-for-in-windows-azure-table-storage.html
            //https://www.red-gate.com/simple-talk/cloud/cloud-data/an-introduction-to-windows-azure-table-storage/

            if(result.GetNewToken < DateTimeOffset.Now)
            {
                result = null;
                log.Info("need a new token");
            }

            Reddit r = null;
            BotWebAgent agent = null;
            bool saveToken = false;
            bool tryLogin = false;
            int tryLoginAttempts = 2;
            do
            {
                tryLoginAttempts--;
                tryLogin = false;
                if (result == null)
                {
                    agent = new BotWebAgent(_user, _pass, _clientId, _secret, "https://www.reddit.com/user/somekindofbot0000/");
                    result = new RedditOAuth() { Token = agent.AccessToken, GetNewToken = DateTimeOffset.Now.AddMinutes(57), PartitionKey = "reddit", RowKey = _user };
                    r = new Reddit(agent, true);
                    saveToken = true;
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
                    catch (WebException w)
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

            if (saveToken)
            {
                oauthTable
                    .Execute(
                        TableOperation.InsertOrReplace(result));
                log.Info("saving token");
            }

            CheckMail(r, log);

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

        private static void Init()
        {
            _storage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            _user = Environment.GetEnvironmentVariable("BotName");
            _pass = Environment.GetEnvironmentVariable("BotPass");
            _secret = Environment.GetEnvironmentVariable("BotSecret");
            _clientId = Environment.GetEnvironmentVariable("botClientId");
            _hasRunInit = true;
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
                if (response?.archived_snapshots?.closest?.available == true)
                {
                    archivedUrl = response.archived_snapshots.closest.url;
                    log.Info("using available snapshot.");
                }
                else
                {
                    log.Info("creating snapshot.");
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



        private static void CheckMail(Reddit r, TraceWriter log)
        {
            if (r.User.HasMail)
            {
                log.Info("has mail");
                string mailBaseAddress = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                using (HttpClient client = new HttpClient())
                {
                    client.BaseAddress = new Uri("https://"+mailBaseAddress);
                    if (!Debug)
                    {
                        
                        log.Info("posting:" + mailBaseAddress+ $"/api/CheckBotMail/name/{r.User.Name}/");
                         var a = client.PostAsync($"/api/CheckBotMail/name/{r.User.Name}/",null).Result;
                        
                    }
                }
            }
        }
    }

    public class RedditOAuth : TableEntity
    {
        public string Token { get; set; }
        public DateTimeOffset GetNewToken { get; set; }
    }
}
