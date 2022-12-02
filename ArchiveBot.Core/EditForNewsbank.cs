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
using ArchiveBot.Core.Objects.NewsBankDependancies;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.Management.Storage.Models;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure;
using WaybackMachineWrapper;

namespace ArchiveBot.Core

{
    public class EditForNewsbank
    {
        private bool _hasRunInit;
        
        
        //private readonly IEZProxySignInCredentialsProvider? _credProvider = null;
        //private readonly bool? _debug;

        private string _user => _botParameterCredentialsProvider.BotName;
        private string _pass => _botParameterCredentialsProvider.BotPassword;
        private string _secret => _botParameterCredentialsProvider.BotSecret;
        private string _clientId => _botParameterCredentialsProvider.BotClientId;
        private readonly IBotParameterCredentialsProvider _botParameterCredentialsProvider;
        private readonly ILogger _log;
        private readonly HttpClient _httpClient;
        private readonly TableServiceClient _tableServiceClient;
        private readonly NewsBankClient _newsBankClient;
        private bool _checkTableExists = true;



        private const string PASSWORD_FORM_PARAMETER = "pass";
        private const string SEARCH_PATH = "apps/news/results";
        private const string LOGIN_FORM_PARAMETER = "user";

        public EditForNewsbank(IEZProxySignInCredentialsProvider eZProxySignInCredentialsProvider,
            IBotParameterCredentialsProvider botParameterCredentialsProvider,
            HttpClient httpClient,
            TableServiceClient tableServiceClient,
            NewsBankClient newsBankClient,
            ILogger log)
        {
            _botParameterCredentialsProvider = botParameterCredentialsProvider;
            //_user = botParameterCredentialsProvider.BotName; //Environment.GetEnvironmentVariable("BotName");
            //_pass = botParameterCredentialsProvider.BotPassword; // Environment.GetEnvironmentVariable("BotPass");
            //_secret = botParameterCredentialsProvider.BotSecret; // Environment.GetEnvironmentVariable("BotSecret");
            //_clientId = botParameterCredentialsProvider.BotClientId; // Environment.GetEnvironmentVariable("botClientId");

            _log = log;

            _httpClient = httpClient;
            _tableServiceClient = tableServiceClient;
            _newsBankClient = newsBankClient;
        }

        
        public async Task RunAsync()
        {


            //CloudTable articleTable = CloudStorageAccount
            //    .Parse(_storage)
            //    .CreateCloudTableClient()
            //    .GetTableReference("article");
            

            if (_checkTableExists)
            {

                _ = await _tableServiceClient.CreateTableIfNotExistsAsync("article");
            }

            var articleTableClient = _tableServiceClient.GetTableClient("article");
            DateTimeOffset todayLocal = DateTimeOffset.Now.Date;
            DateTime today = todayLocal.ToUniversalTime().DateTime;
            AsyncPageable<ArticlePost> articlesPublishedBeforeToday = articleTableClient.QueryAsync<ArticlePost>(x => x.ArticleDate < today && x.ArticleDate > today.AddDays(-27));


            //TableQuery<ArticlePost> articlesPublishedBeforeToday = articleTableClient.CreateQuery<ArticlePost>().Where(x => x.ArticleDate < today && x.ArticleDate > today.AddDays(-27)).AsTableQuery();

            


            //CloudTable oauthTable = CloudStorageAccount
            //    .Parse(_storage)
            //    .CreateCloudTableClient()
            //    .GetTableReference("oauth");

            if (_checkTableExists)
            {
                _checkTableExists = false;
                _ = await _tableServiceClient.CreateTableIfNotExistsAsync("oauth");
            }

            var oauthTableClient = _tableServiceClient.GetTableClient("oauth");



            RedditOAuth? result = await oauthTableClient.GetEntityAsync<RedditOAuth>("reddit", _user);
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

            //<Azure.Page<ArticlePost>, IAsyncEnumerableArticlePost>
            int count = 0;
            await foreach (ArticlePost ap in articlesPublishedBeforeToday)
            {
                Task updateComment = GetCommentLine(ap)
                    .ContinueWith( async (commentLine) => 
                    {
                        bool retry = false;
                        if (ap.CommentUri == null) return;
                        do
                        {
                            Comment c = await r.GetCommentAsync(new Uri("https://oauth.reddit.com/" + ap.CommentUri)).ConfigureAwait(false);
                            if (!String.IsNullOrEmpty(commentLine.Result))
                            {
                                await EditComment(commentLine.Result, c);
                                await articleTableClient.DeleteEntityAsync(ap.PartitionKey,ap.RowKey);
                                //articleTableClient.Execute(TableOperation.Delete(ap));
                                retry = false;
                            }
                            else
                            {
                                _log.LogInformation("Empty CommentLine, will check headline.");
                                retry = await TryUpdateArticleData(ap, r, articleTableClient);
                                if (retry)
                                {
                                    _log.LogInformation($"author: {ap.ArticleAuthor} ---- headline: {ap.ArticleHeadline}");
                                }

                            }
                        } while (retry);
                    }
                    ,TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();

                await updateComment.ConfigureAwait(false);//one at a time due to rate issue
                count++;
            }
            _log.LogInformation($"edited {count} comments");
        }


        internal async Task<string> GetCommentLine(ArticlePost articlePost)
        {
            _log.LogInformation("GetCommentLine");          
            
            SearchResult searchResult = null;
            try
            {
                var localDate = TimeZoneInfo.ConvertTimeFromUtc(articlePost.ArticleDate, TimeZoneInfo.Local).ToShortDateString();
                searchResult = await _newsBankClient.Search(
                        new SearchRequest()
                        {
                            Product = Product.WorldNews,
                            Publications = new List<Publication>() { Publication.SeattleTimesWebEditionArticles },
                            SearchParameter0 = new SearchParameter() { Field = SearchField.Author, Value = articlePost.ArticleAuthor?.Replace("/",string.Empty) },
                            SearchParameter1 = new SearchParameter() { Field = SearchField.Headline, Value = $"\"{articlePost.ArticleHeadline}\"", ParameterCompoundOperator = CompoundOperator.AND },
                            SearchParameter2 = new SearchParameter() { Field = SearchField.Date, Value = localDate, ParameterCompoundOperator = CompoundOperator.AND }
                        });
            }
            catch (NullReferenceException nullRefEx)  //not the best option.
            {
                _log.LogError("possible no Web edition result, " + articlePost.CommentUri);
                _log.LogError(nullRefEx.Message);
                _log.LogError(nullRefEx.StackTrace);
                return string.Empty;
            }
            catch(Exception e)
            {
                _log.LogError(e,"newsbank search error");
                throw;
            }
            return $"[NewsBank version]({searchResult.FirstSearchResultItem.ResultItemUri}) via SPL [^(SPL) ^(account) ^(required)](https://www.spl.org/using-the-library/get-started/get-started-with-a-library-card/library-card-application)";
        }

        private async Task<bool> TryUpdateArticleData(ArticlePost articlePost, Reddit r, TableClient articleTableClient)
        {
            bool result = false;
            Comment comment = await r.GetCommentAsync(new Uri("https://oauth.reddit.com" + articlePost.CommentUri));
            Post post = (Post)comment.Parent;

            string articleUrl = post.Url.GetComponents(UriComponents.Host | UriComponents.Path | UriComponents.Scheme, UriFormat.SafeUnescaped);
            using (HttpResponseMessage articleResponse = await _httpClient.GetAsync(articleUrl))
            {
                SeattleTimesArticle seattleTimesArticle = new SeattleTimesArticle(articleResponse);
                if(seattleTimesArticle.Headline != articlePost.ArticleHeadline)
                {
                    _log.LogInformation("new headline: " + seattleTimesArticle.Headline);
                    articlePost.ArticleHeadline = seattleTimesArticle.Headline;
                    result = true;
                }
                if(seattleTimesArticle.ByLineAuthors.FirstOrDefault() != articlePost.ArticleAuthor)
                {
                    string? author = seattleTimesArticle.ByLineAuthors.FirstOrDefault();
                    _log.LogInformation("new author: " + author);
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

        internal static async Task EditComment(string commentLine, Comment? comment)
        {
            if(comment == null)
            {
                throw new ArgumentNullException(nameof(comment));
            }
            await comment.EditTextAsync(comment.Body.Replace("&#65279;&#xFEFF;", commentLine).Replace(":0:", commentLine));//[NewsBank version via SPL]({""})^[SPL account required]()
        }
    }
}
