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
using Czf.Api.NewsBankWrapper;
using ArchiveBot.Objects.NewsBankDependancies;
using System.Threading;
using Microsoft.Extensions.Logging;

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
        private static HttpClient client;
        private static bool checkTableExists = false;

        
        [FunctionName("ArchiveBot")]
        public static async Task Run([TimerTrigger("00 */10 * * * *")]TimerInfo myTimer, ILogger log)
        {
            
            if (!_hasRunInit)
            {
                Init();
                
            }

            
            CloudTable oauthTable = CloudStorageAccount
                .Parse(_storage)
                .CreateCloudTableClient()
                .GetTableReference("oauth");
            
            if (checkTableExists)
            {
                oauthTable.CreateIfNotExists();
            }

            CloudTable articleTable = CloudStorageAccount
                            .Parse(_storage)
                            .CreateCloudTableClient()
                            .GetTableReference("article");

            if (checkTableExists)
            {
                articleTable.CreateIfNotExists();
                checkTableExists = true;
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
                log.LogInformation("need a new token");
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
                log.LogInformation("saving token");
            }

            NewsBankClient newsBankClient = new NewsBankClient(
                 new EnvironmentVariableEZProxySignInUriProvider(),
                EditForNewsbank._credProvider,
                new EnvironmentVariableProductBaseUriProvider(),
                new BasicCanLog(log));




            Task<HttpResponseMessage> checkMailTask = CheckMail(r, log, client);

            //https://www.reddit.com/r/SeattleWA/search?q=%28+site%3Aseattletimes.com+Subreddit%3ASeattleWA+%29&sort=new&t=day
            Listing<Post> posts =
                r.AdvancedSearch(x => x.Subreddit == "SeattleWA" &&
                x.Site == "seattletimes.com"
            , Sorting.New, TimeSorting.Day);

            bool allPostsSuccess = true;
            using (WaybackClient waybackMachine = new WaybackClient())
            {
                foreach (Post p in posts.TakeWhile(x => !x.IsHidden))
                {
                    allPostsSuccess &= await ProcessPost(p, waybackMachine, log, articleTable, newsBankClient);
                }

            }
            if (checkMailTask.Status < TaskStatus.RanToCompletion)
            {
                log.LogInformation("waiting for checkmail");
                await checkMailTask;
            }
            else
            {
                log.LogInformation(checkMailTask.Status.ToString());
            }
            

            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

            if (!allPostsSuccess)
            {
                throw new ApplicationException("Not all Posts were processed successfully");
            }
        }

        private static void Init()
        {
            _storage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            _user = Environment.GetEnvironmentVariable("BotName");
            _pass = Environment.GetEnvironmentVariable("BotPass");
            _secret = Environment.GetEnvironmentVariable("BotSecret");
            _clientId = Environment.GetEnvironmentVariable("botClientId");
            _hasRunInit = true;
            client = new HttpClient();
            //Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:73.0)
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:73.0)");

            string mailBaseAddress = Environment.GetEnvironmentVariable("WEBSITE_HOSTNAME");
            client.BaseAddress = new Uri("https://" + mailBaseAddress);
        }


        private static async Task<bool> ProcessPost(Post p, WaybackClient waybackClient, ILogger log, CloudTable articleTable, NewsBankClient newsBankClient)
        {
            bool successProcessPost = true;
            try
            {
                if (!Debug)
                {
                    p.Hide();
                }
                Uri archivedUrl = null;
                log.LogInformation(p.Url.ToString());
                Uri target = new Uri(p.Url.GetComponents(UriComponents.Host | UriComponents.Path | UriComponents.Scheme, UriFormat.SafeUnescaped));
                using (Task<AvailableResponse> response = waybackClient.AvailableAsync(target))
                using (Task<HttpResponseMessage> targetGetResponse = client.GetAsync(target))
                {
                    Task<Comment> commentTask = response.ContinueWith(async x =>
                    {
                        AvailableResponse availableResponse = x.Result;

                        short attempts = 2;
                        bool success = false;
                        do
                        {
                            attempts--;
                            if (availableResponse?.archived_snapshots?.closest?.available == true)
                            {
                                archivedUrl = availableResponse.archived_snapshots.closest.url;
                                log.LogInformation("using available snapshot.");
                                success = true;
                            }
                            else
                            {
                                log.LogInformation("creating snapshot.");
                                archivedUrl = await waybackClient.SaveAsync(target);
                                short validationAttempts = 2;
                                do
                                {
                                    validationAttempts--;
                                    using (HttpResponseMessage responseCheck = await client.GetAsync(archivedUrl))
                                    {
                                        if (!responseCheck.IsSuccessStatusCode || responseCheck.StatusCode == HttpStatusCode.NotFound)
                                        {
                                            log.LogWarning($"404 returned from archive.org using provided response url. \nstatuscode:{responseCheck.StatusCode}  \narchiveURL:{archivedUrl}");
                                            Thread.Sleep(100);
                                        }
                                        else
                                        {
                                            log.LogInformation("check returned success.");
                                            success = true;
                                        }
                                    }
                                } while (validationAttempts > 0 && !success);
                            }
                        } while (attempts > 0 && !success);
                        if (!success)
                        {
                            successProcessPost = false;
                            throw new ApplicationException("Wayback machine wouldn't cache content.");
                        }

                        string msg =
            $@"[Archive.org version.]({archivedUrl.ToString()})

:0:

----
^^You ^^can ^^support ^^Archive.org ^^via [^^(Amazon) ^^(Smile)](https://smile.amazon.com/ch/94-3242767)  
^^You ^^can ^^support ^^Seattle ^^Public ^^Library ^^via [^^(Amazon) ^^(Smile)](https://smile.amazon.com/ch/91-1140642)  
^^I'm ^^a ^^bot, ^^beep ^^boop [ ^^((fork) ^^(me) ^^(on) ^^(github))](https://github.com/czf/ArchiveBot)";

                        log.LogInformation(msg);


                        Comment comment = null;
                        if (!Debug)
                        {
                            comment = p.Comment(msg);
                        }
                        return comment;
                    }, TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();
                    Comment c = await commentTask;
                    await Task.WhenAll(targetGetResponse, commentTask).ContinueWith(
                        x =>
                        {
                            log.LogInformation("start newsbank");
                            Comment comment = commentTask.Result;
                            using (HttpResponseMessage articleResponse = targetGetResponse.Result)
                            {
                                if (articleResponse == null)
                                {
                                    log.LogInformation("articleResponse is null");
                                }
                                SeattleTimesArticle seattleTimesArticle = new SeattleTimesArticle(articleResponse);
                                if (seattleTimesArticle.PublishDate.Date < DateTime.Now.Date)
                                {
                                    log.LogInformation("article post is at least a day old, will make newsbank edit.");
                                    EditForNewsbank.GetCommentLine(new ArticlePost(seattleTimesArticle, comment), log, newsBankClient
                                        ).ContinueWith(y =>
                                        {
                                            if (!String.IsNullOrEmpty(y.Result))
                                            {
                                                EditForNewsbank.EditComment(y.Result, comment);
                                                log.LogInformation("article post has been edited.");
                                            }
                                            else
                                            {
                                                log.LogInformation("commentline null or empty will store article post");
                                                articleTable.Execute(TableOperation.InsertOrReplace(new ArticlePost(seattleTimesArticle, comment)));
                                            }
                                        });

                                }
                                else
                                {
                                    log.LogInformation("will store article post");
                                    articleTable.Execute(TableOperation.InsertOrReplace(new ArticlePost(seattleTimesArticle, comment)));
                                }
                            }

                        }, TaskContinuationOptions.OnlyOnRanToCompletion);

                    //TODO Dispose AvailableResponse;

                }
            }
            catch(Exception e)
            {
                log.LogError("", e);
                successProcessPost = false;
            }
            return successProcessPost;
        }


        private static Task<HttpResponseMessage> CheckMail(Reddit r, ILogger log, HttpClient client)
        {
            Task<HttpResponseMessage> result = Task.FromResult<HttpResponseMessage>(null);
            if (r.User.HasMail)
            {
                log.LogInformation("has mail");
                
                    if (!Debug)
                    {
                        result = client.PostAsync($"/api/CheckBotMail/name/{r.User.Name}/", null);
                        log.LogInformation("posting:" + client.BaseAddress + $"/api/CheckBotMail/name/{r.User.Name}/ \n {result.Status}");
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

        public ArticlePost() { }
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
