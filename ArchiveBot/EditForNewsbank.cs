using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using RedditSharp;
using RedditSharp.Things;
using Czf.Api.NewsBankWrapper;
using Czf.Domain.NewsBankWrapper.Objects;
using Czf.Domain.NewsBankWrapper.Enum;
using System.Threading;
using System.Threading.Tasks;
using ArchiveBot.Core.Objects;
using Czf.Domain.NewsBankWrapper.Interfaces;
using ArchiveBot.Core.Objects.NewsBankDependancies.ignore;
using ArchiveBot.Core.Objects.NewsBankDependancies;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Management.Storage.Models;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure;

namespace ArchiveBot
{
    public static class EditForNewsbank
    {
        private static bool _hasRunInit;
        
        
        internal static IEZProxySignInCredentialsProvider _credProvider = null;
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
        private static bool checkTableExists = true;



        private const string PASSWORD_FORM_PARAMETER = "pass";
        private const string SEARCH_PATH = "apps/news/results";
        private const string LOGIN_FORM_PARAMETER = "user";

        [FunctionName("EditForNewsbank")]
        public static async Task Run([TimerTrigger("0 0 07 * * *")]TimerInfo myTimer, ILogger log)
        {
            NewsBankClient newsBankClient = new NewsBankClient(
                new EnvironmentVariableEZProxySignInUriProvider(),
                _credProvider,
                new EnvironmentVariableProductBaseUriProvider(),
                new BasicCanLog(log)
                );
    
            var serviceClient = new TableServiceClient(
                new Uri(_storage),
                new TableSharedKeyCredential(/*accountName*/ null, /*StorageAccountKey*/ null));

            

            //CloudTable articleTable = CloudStorageAccount
            //    .Parse(_storage)
            //    .CreateCloudTableClient()
            //    .GetTableReference("article");
            

            if (checkTableExists)
            {

                _ = await serviceClient.CreateTableIfNotExistsAsync("article");
            }

            var articleTableClient = serviceClient.GetTableClient("article");
            
            DateTime today = new DateTime(DateTime.Today.Ticks, DateTimeKind.Utc);
            AsyncPageable<ArticlePost> articlesPublishedBeforeToday = articleTableClient.QueryAsync<ArticlePost>(x => x.ArticleDate < today && x.ArticleDate > today.AddDays(-27));


            //TableQuery<ArticlePost> articlesPublishedBeforeToday = articleTableClient.CreateQuery<ArticlePost>().Where(x => x.ArticleDate < today && x.ArticleDate > today.AddDays(-27)).AsTableQuery();

            


            //CloudTable oauthTable = CloudStorageAccount
            //    .Parse(_storage)
            //    .CreateCloudTableClient()
            //    .GetTableReference("oauth");

            if (checkTableExists)
            {
                checkTableExists = false;
                _ = await serviceClient.CreateTableIfNotExistsAsync("oauth");
            }

            var oauthTableClient = serviceClient.GetTableClient("oauth");



            RedditOAuth result = await oauthTableClient.GetEntityAsync<RedditOAuth>("reddit", _user);
                //.Execute(
                //    TableOperation.Retrieve<RedditOAuth>("reddit", _user)
                //).Result;
            //https://blog.maartenballiauw.be/post/2012/10/08/what-partitionkey-and-rowkey-are-for-in-windows-azure-table-storage.html
            //https://www.red-gate.com/simple-talk/cloud/cloud-data/an-introduction-to-windows-azure-table-storage/

            if (result?.GetNewToken < DateTimeOffset.Now)
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
                await oauthTableClient.UpsertEntityAsync(result);
                    //.Execute(
                    //    TableOperation.InsertOrReplace(result));
                log.LogInformation("saving token");
            }
            IEnumerable<Azure.Page<ArticlePost>> f = null;
            var p = f.SelectMany(x => x.Values);
            List<Task> updateCommentTasks = new List<Task>();
            //<Azure.Page<ArticlePost>, IAsyncEnumerableArticlePost>
            
            await foreach (ArticlePost ap in articlesPublishedBeforeToday)
            {
                Task updateComment = GetCommentLine(ap, log, newsBankClient)
                    .ContinueWith( async (commentLine) => 
                    {
                        bool retry = false;
                        do
                        {
                            Comment c = r.GetComment(new Uri("https://www.reddit.com" + ap.CommentUri));
                            if (!String.IsNullOrEmpty(commentLine.Result))
                            {
                                EditComment(commentLine.Result, c);
                                await articleTableClient.DeleteEntityAsync(ap.PartitionKey,ap.RowKey);
                                //articleTableClient.Execute(TableOperation.Delete(ap));
                                retry = false;
                            }
                            else
                            {
                                log.LogInformation("Empty CommentLine, will check headline.");
                                retry = await TryUpdateArticleData(ap, r, articleTableClient, log);
                                if (retry)
                                {
                                    log.LogInformation($"author: {ap.ArticleAuthor} ---- headline: {ap.ArticleHeadline}");
                                }

                            }
                        } while (retry);
                    }
                    ,TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();

                updateCommentTasks.Add(updateComment);
            }
                        
            await Task.WhenAll(updateCommentTasks.ToArray());
            log.LogInformation("AwaitWhenALL " + updateCommentTasks.Count.ToString());
        }

        static EditForNewsbank()
        {
            _storage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            _user = Environment.GetEnvironmentVariable("BotName");
            _pass = Environment.GetEnvironmentVariable("BotPass");
            _secret = Environment.GetEnvironmentVariable("BotSecret");
            _clientId = Environment.GetEnvironmentVariable("botClientId");
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:73.0)");

            

            if (Debug)
            {
                _credProvider = new CredProvider();
            }
            else
            {
                _credProvider = new AzureKeyVaultEZProxySignInCredentialsProvider();
            }

            
            _hasRunInit = true;
        }

        internal static async Task<string> GetCommentLine(ArticlePost articlePost, ILogger log, NewsBankClient newsBankClient)
        {
            log.LogInformation("GetCommentLine");          
            
            SearchResult searchResult = null;
            try
            {
                searchResult = await newsBankClient.Search(
                        new SearchRequest()
                        {
                            Product = Product.WorldNews,
                            Publications = new List<Publication>() { Publication.SeattleTimesWebEditionArticles },
                            SearchParameter0 = new SearchParameter() { Field = SearchField.Author, Value = articlePost.ArticleAuthor.Replace("/",string.Empty) },
                            SearchParameter1 = new SearchParameter() { Field = SearchField.Headline, Value = $"\"{articlePost.ArticleHeadline}\"", ParameterCompoundOperator = CompoundOperator.AND },
                            SearchParameter2 = new SearchParameter() { Field = SearchField.Date, Value = articlePost.ArticleDate.ToShortDateString(), ParameterCompoundOperator = CompoundOperator.AND }
                        });
            }
            catch (NullReferenceException nullRefEx)  //not the best option.
            {
                log.LogError("possible no Web edition result, " + articlePost.CommentUri);
                log.LogError(nullRefEx.Message);
                log.LogError(nullRefEx.StackTrace);
                return string.Empty;
            }
            catch(Exception e)
            {
                log.LogError(e.Message);
                throw;
            }
            return $"[NewsBank version]({searchResult.FirstSearchResultItem.ResultItemUri}) via SPL [^(SPL) ^(account) ^(required)](https://www.spl.org/using-the-library/get-started/get-started-with-a-library-card/library-card-application)";
        }

        private static async Task<bool> TryUpdateArticleData(ArticlePost articlePost, Reddit r, TableClient articleTableClient, ILogger log)
        {
            bool result = false;
            Comment comment = r.GetComment(new Uri("https://www.reddit.com" + articlePost.CommentUri));
            Post post = (Post)comment.Parent;

            string articleUrl = post.Url.GetComponents(UriComponents.Host | UriComponents.Path | UriComponents.Scheme, UriFormat.SafeUnescaped);
            using (HttpResponseMessage articleResponse = await client.GetAsync(articleUrl))
            {
                SeattleTimesArticle seattleTimesArticle = new SeattleTimesArticle(articleResponse);
                if(seattleTimesArticle.Headline != articlePost.ArticleHeadline)
                {
                    log.LogInformation("new headline: " + seattleTimesArticle.Headline);
                    articlePost.ArticleHeadline = seattleTimesArticle.Headline;
                    result = true;
                }
                if(seattleTimesArticle.ByLineAuthors.FirstOrDefault() != articlePost.ArticleAuthor)
                {
                    string author = seattleTimesArticle.ByLineAuthors.FirstOrDefault();
                    log.LogInformation("new author: " + author);
                    articlePost.ArticleAuthor = author;
                    result = true;
                }
                if (result)
                {
                    //await articleTable.ExecuteAsync(TableOperation.InsertOrReplace(articlePost));
                    await articleTableClient.UpsertEntityAsync(articlePost);
                }
            }
            return result;
        }

        internal static void EditComment(string commentLine, Comment comment)
        {
            comment.EditText(comment.Body.Replace(":0:", commentLine));//[NewsBank version via SPL]({""})^[SPL account required]()
            comment.Save();
        }
    }
}
