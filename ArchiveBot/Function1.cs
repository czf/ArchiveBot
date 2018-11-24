/*
New York Times Online => Proquest
Seattle Times Online => NewsBank
WallStreetJournal Online => Proquest
*/

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
using ArchiveBot.Objects;

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
        private static HttpClient client = new HttpClient();
        private static bool checkTableExists = false;


        [FunctionName("ArchiveBot")]
        public static async Task Run([TimerTrigger("00 */1 * * * *")]TimerInfo myTimer, TraceWriter log)
        {
            
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

            if(result?.GetNewToken < DateTimeOffset.Now)
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
            
            using (HttpClient client = new HttpClient())
            {
                string mailBaseAddress = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
                client.BaseAddress = new Uri("https://" + mailBaseAddress);
                Task<HttpResponseMessage> checkMailTask = CheckMail(r, log, client);

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
                if (checkMailTask.Status < TaskStatus.RanToCompletion)
                {
                    log.Info("waiting for checkmail");
                    await checkMailTask;
                }
                else
                {
                    log.Info(checkMailTask.Status.ToString());
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
                using (Task<AvailableResponse> response = waybackClient.AvailableAsync(target))
                using (Task<HttpResponseMessage> targetGetResponse = client.GetAsync(target))
                {


                    Task<Comment> commentTask = response.ContinueWith(async x =>
                    {
                        AvailableResponse availableResponse = x.Result;

                        int attempts = 2;
                        bool success = false;
                        do
                        {
                            attempts--;
                            if (availableResponse?.archived_snapshots?.closest?.available == true)
                            {
                                archivedUrl = availableResponse.archived_snapshots.closest.url;
                                log.Info("using available snapshot.");
                                success = true;
                            }
                            else
                            {
                                log.Info("creating snapshot.");
                                archivedUrl = await waybackClient.SaveAsync(target);

                                HttpResponseMessage responseCheck = await client.GetAsync(archivedUrl);
                                if (!responseCheck.IsSuccessStatusCode || responseCheck.StatusCode == HttpStatusCode.NotFound)
                                {
                                    log.Warning($"404 returned from archive.org using provided response url. \nstatuscode:{responseCheck.StatusCode}  \narchiveURL:{archivedUrl}");
                                }
                                else
                                {
                                    log.Info("check returned success.");
                                    success = true;
                                }

                            }
                        } while (attempts > 0 && !success);
                        if (!success)
                        {
                            throw new ApplicationException("Wayback machine wouldn't cache content.");
                        }

                        string msg =
            $@"[Archive.org version.]({archivedUrl.ToString()})

----
^^I'm ^^a ^^bot, ^^beep ^^boop";

                        log.Info(msg);


                        Comment comment = null;
                        if (!Debug)
                        {
                            comment = p.Comment(msg);
                        }
                        return comment;
                    }, TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();

                    await Task.WhenAll(targetGetResponse, commentTask).ContinueWith(
                        x =>

                    {
                        Comment comment = commentTask.Result;
                        using (HttpResponseMessage articleResponse = targetGetResponse.Result)
                        {
                            SeattleTimesArticle seattleTimesArticle = new SeattleTimesArticle(articleResponse);

                            CloudTable articleTable = CloudStorageAccount
                            .Parse(_storage)
                            .CreateCloudTableClient()
                            .GetTableReference("article");

                            if (checkTableExists)
                            {
                                articleTable.CreateIfNotExists();
                            }
                            articleTable.Execute(TableOperation.InsertOrReplace(new ArticlePost(seattleTimesArticle, comment)));
                        }

                    }, TaskContinuationOptions.OnlyOnRanToCompletion);

                    //TODO Dispose AvailableResponse;

                }
            }
            catch(Exception e)
            {
                log.Error("", e);
                throw;
            }
        }


        private static Task<HttpResponseMessage> CheckMail(Reddit r, TraceWriter log, HttpClient client)
        {
            Task<HttpResponseMessage> result = Task.FromResult<HttpResponseMessage>(null);
            if (r.User.HasMail)
            {
                log.Info("has mail");
                
                    if (!Debug)
                    {
                        result = client.PostAsync($"/api/CheckBotMail/name/{r.User.Name}/", null);
                        log.Info("posting:" + client.BaseAddress + $"/api/CheckBotMail/name/{r.User.Name}/ \n {result.Status}");
                    }
            }
            return result;
        }
    }
    public class ArticlePost : TableEntity
    {
        public string ArticleAuthor { get; set; }
        public string ArticleHeadline { get; set; }
        public DateTime ArticleDate { get; set; }
        public string CommentUri { get; set; }

        public ArticlePost(SeattleTimesArticle seattleTimesArticle, Comment comment)
        {
            ArticleAuthor = seattleTimesArticle.ByLineAuthors.First();
            ArticleHeadline = seattleTimesArticle.Headline;
            ArticleDate = seattleTimesArticle.PublishDate.Date;
            CommentUri = comment.Permalink;
            PartitionKey = comment.Subreddit;
            RowKey = comment.Id;
            
        }
    }

    public class RedditOAuth : TableEntity
    {
        public string Token { get; set; }
        public DateTimeOffset GetNewToken { get; set; }
    }
}
