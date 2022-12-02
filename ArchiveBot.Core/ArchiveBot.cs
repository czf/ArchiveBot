using ArchiveBot.Core.Objects.NewsBankDependancies;
using Azure.Data.Tables;
using Czf.Api.NewsBankWrapper;
using Microsoft.Azure.Management.Storage.Models;
using RedditSharp.Things;
using RedditSharp;
using System.Net;
using System.Security.Authentication;
using WaybackMachineWrapper;
using Czf.Domain.NewsBankWrapper.Interfaces;
using ArchiveBot.Core.Objects;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ArchiveBot.Core
{
    public class ArchiveBot
    {

        //private readonly string _user;
        //private readonly string _pass;
        //private readonly string _secret;
        //private readonly string _clientId;
        private string _user => _botParameterCredentialsProvider.BotName;
        private string _pass => _botParameterCredentialsProvider.BotPassword;
        private string _secret => _botParameterCredentialsProvider.BotSecret;
        private string _clientId => _botParameterCredentialsProvider.BotClientId;
        private readonly IBotParameterCredentialsProvider _botParameterCredentialsProvider;

        private readonly ILogger _log;
        private readonly HttpClient _httpClient;
        private readonly TableServiceClient _tableServiceClient;
        private readonly NewsBankClient _newsBankClient;
        private readonly WaybackClient _waybackClient;
        private readonly EditForNewsbank _editForNewsbank;
        private readonly CheckBotMail _checkBotMail;
        private bool _checkTableExists = true;


        private static bool? _debug;
        public static bool Debug
        {
            get
            {
                if (!_debug.HasValue)
                {
                    try
                    {
#if DEBUG
                        _debug = true;
#else
                        _debug = false;
#endif
                    }
                    catch(Exception e)
                    {
                        _debug = false;
                    }
                }
                return _debug.Value;
            }
        }


        public ArchiveBot(IEZProxySignInCredentialsProvider eZProxySignInCredentialsProvider, 
            IBotParameterCredentialsProvider botParameterCredentialsProvider,
            HttpClient httpClient,
            TableServiceClient tableServiceClient,
            NewsBankClient newsBankClient,
            WaybackClient waybackClient,
            EditForNewsbank editForNewsbank,
            CheckBotMail checkBotMail,
            ILogger log)
        {
            //_user = botParameterCredentialsProvider.BotName; //Environment.GetEnvironmentVariable("BotName");
            //_pass = botParameterCredentialsProvider.BotPassword; // Environment.GetEnvironmentVariable("BotPass");
            //_secret = botParameterCredentialsProvider.BotSecret; // Environment.GetEnvironmentVariable("BotSecret");
            //_clientId = botParameterCredentialsProvider.BotClientId; // Environment.GetEnvironmentVariable("botClientId");
            _botParameterCredentialsProvider = botParameterCredentialsProvider;
            _log = log;

            _httpClient = httpClient;
            _tableServiceClient = tableServiceClient;
            _newsBankClient = newsBankClient;
            _waybackClient = waybackClient;
            _editForNewsbank = editForNewsbank;
            _checkBotMail = checkBotMail;
        }

        public async Task RunAsync()
        {
            if (_checkTableExists)
            {
                _ = await _tableServiceClient.CreateTableIfNotExistsAsync("oauth");
            }
            var oauthTableClient = _tableServiceClient.GetTableClient("oauth");



            if (_checkTableExists)
            {
                _ = await _tableServiceClient.CreateTableIfNotExistsAsync("article");
                _checkTableExists = false;
            }
            var articleTableClient = _tableServiceClient.GetTableClient("article");

            RedditOAuth? result = await oauthTableClient.GetEntityAsync<RedditOAuth>("reddit", _user);
            //(RedditOAuth)oauthTable
            //.Execute(
            //    TableOperation.Retrieve<RedditOAuth>("reddit", _user)
            //).Result;
            //https://blog.maartenballiauw.be/post/2012/10/08/what-partitionkey-and-rowkey-are-for-in-windows-azure-table-storage.html
            //https://www.red-gate.com/simple-talk/cloud/cloud-data/an-introduction-to-windows-azure-table-storage/

            if (result?.GetNewToken < DateTimeOffset.Now)
            {
                result = null;
                _log.LogInformation("need a new token");
            }

            Reddit? r = null;
            BotWebAgent? agent = null;
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
                        while(r.User == null)
                        {
                            await Task.Delay(300);
                        }
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
                await oauthTableClient.UpsertEntityAsync(result);
                //.Execute(
                //    TableOperation.InsertOrReplace(result));
                _log.LogInformation("saving token");
            }

            //NewsBankClient newsBankClient = new NewsBankClient(
            //     new EnvironmentVariableEZProxySignInUriProvider(),
            //    EditForNewsbank._credProvider,
            //    new EnvironmentVariableProductBaseUriProvider(),
            //    new BasicCanLog(_log));




            Task checkMailTask = CheckMail(r);
            

            //https://www.reddit.com/r/SeattleWA/search?q=%28+site%3Aseattletimes.com+Subreddit%3ASeattleWA+%29&sort=new&t=day
            Listing<Post> posts =
                r.AdvancedSearch(x => x.Subreddit == "Seattle" &&
                x.Site == "seattletimes.com"
            , Sorting.New, TimeSorting.Day);

            bool allPostsSuccess = true;
            using (WaybackClient waybackMachine = new WaybackClient())
            {
                await foreach (Post p in posts.Where(x => !x.IsHidden))
                {
                    allPostsSuccess &= await ProcessPost(p, articleTableClient);
                }

            }

            
            if (checkMailTask.Status < TaskStatus.RanToCompletion)
            {
                _log.LogInformation("waiting for checkmail");
                await checkMailTask;
            }
            else
            {
                _log.LogInformation(checkMailTask.Status.ToString());
            }
            


            _log.LogInformation($"archive bot completed at: {DateTime.Now}");

            if (!allPostsSuccess)
            {
                throw new ApplicationException("Not all Posts were processed successfully");
            }

        }


        private async Task<bool> ProcessPost(Post p, TableClient articleTableClient)
        {
            bool successProcessPost = true;
            try
            {
                if (!Debug)
                {
                    await p.HideAsync();
                }
                Uri archivedUrl = null;
                _log.LogInformation(p.Url.ToString());
                Uri target = new Uri(p.Url.GetComponents(UriComponents.Host | UriComponents.Path | UriComponents.Scheme, UriFormat.SafeUnescaped));
                using (Task<AvailableResponse> response = _waybackClient.AvailableAsync(target))
                using (Task<HttpResponseMessage> targetGetResponse = _httpClient.GetAsync(target))
                {
                    try
                    {
                        Task<Comment?> commentTask = response.ContinueWith(async x =>
                        {
                            AvailableResponse availableResponse = x.Result;

                            short attempts = 2;
                            bool success = false;
                            do
                            {
                                attempts--;
                                if (attempts < 2)
                                {
                                    Thread.Sleep(5000);
                                }
                                if (availableResponse?.archived_snapshots?.closest?.available == true)
                                {
                                    archivedUrl = availableResponse.archived_snapshots.closest.url;
                                    _log.LogInformation("using available snapshot.");
                                    success = true;
                                }
                                else
                                {
                                    _log.LogInformation("creating snapshot.");
                                    archivedUrl = await _waybackClient.SaveAsyncV2(target);
                                    short validationAttempts = 2;
                                    do
                                    {
                                        validationAttempts--;
                                        try
                                        {
                                            using (HttpResponseMessage responseCheck = await _httpClient.GetAsync(archivedUrl))
                                            {
                                                if (!responseCheck.IsSuccessStatusCode || responseCheck.StatusCode == HttpStatusCode.NotFound)
                                                {
                                                    _log.LogWarning($"404 returned from archive.org using provided response url. \nstatuscode:{responseCheck.StatusCode}  \narchiveURL:{archivedUrl}");
                                                    Thread.Sleep(100);
                                                }
                                                else
                                                {
                                                    _log.LogInformation("check returned success.");
                                                    success = true;
                                                }
                                            }
                                        }
                                        catch(Exception validationException)
                                        {
                                            _log.LogError(validationException, $"archivedUrl: {archivedUrl}");
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

                            _log.LogInformation(msg);


                            Comment? comment = null;
                            if (!Debug)
                            {
                                comment = await p.CommentAsync(msg);
                            }
                            return comment;
                        }, TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();
                        Comment? c = await commentTask;
                        await Task.WhenAll(targetGetResponse, commentTask).ContinueWith(
                           async x =>
                            {
                                _log.LogInformation("start newsbank");
                                Comment? comment = commentTask.Result;
                                using (HttpResponseMessage? articleResponse = await targetGetResponse)
                                {
                                    if (articleResponse == null)
                                    {
                                        _log.LogInformation("articleResponse is null");
                                    }
                                    SeattleTimesArticle seattleTimesArticle = new SeattleTimesArticle(articleResponse);
                                    if (seattleTimesArticle.PublishDate.Date < DateTime.Now.Date)
                                    {
                                        _log.LogInformation("article post is at least a day old, will make newsbank edit.");
                                        await _editForNewsbank.GetCommentLine(new ArticlePost(seattleTimesArticle, comment)
                                            ).ContinueWith(async y =>
                                            {
                                                if (!String.IsNullOrEmpty(y.Result))
                                                {
                                                    await EditForNewsbank.EditComment(y.Result, comment);
                                                    _log.LogInformation("article post has been edited.");
                                                }
                                                else
                                                {
                                                    _log.LogInformation("commentline null or empty will store article post");
                                                    try
                                                    {
                                                        await articleTableClient.UpsertEntityAsync(
                                                            new ArticlePost(seattleTimesArticle, comment));
                                                    }
                                                    catch(Exception ex)
                                                    {
                                                        _log.LogError(ex.ToString());
                                                    }
                                                    //articleTableClient.Execute(TableOperation.InsertOrReplace(new ArticlePost(seattleTimesArticle, comment)));
                                                }
                                            });

                                    }
                                    else
                                    {
                                        _log.LogInformation("will store article post");
                                        //articleTableClient.Execute(TableOperation.InsertOrReplace(new ArticlePost(seattleTimesArticle, comment)));
                                        try
                                        {
                                            await articleTableClient.UpsertEntityAsync(
                                                new ArticlePost(seattleTimesArticle, comment));
                                        }
                                        catch (Exception ex)
                                        {
                                            _log.LogError(ex.ToString());
                                        }
                                    }
                                }

                            }, TaskContinuationOptions.OnlyOnRanToCompletion);

                    }
                    catch(Exception ex)
                    {
                        _log.LogError(ex, "");
                    }
                    //TODO Dispose AvailableResponse;

                }
            }
            catch (Exception e)
            {
                _log.LogError("", e);
                successProcessPost = false;
            }
            if(!successProcessPost && !Debug)
            {
                await p.UnhideAsync();
            }
            return successProcessPost;
        }

        private Task CheckMail(Reddit r)
        {
            if (r.User.HasMail)
            {
                _log.LogInformation("has mail");

                return _checkBotMail.RunAsync();
            }
            return Task.CompletedTask;
        }
    }
    

}