using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using RedditSharp;
using RedditSharp.Things;
using Czf.Api.NewsBankWrapper;
using Czf.Domain.NewsBankWrapper.Objects;
using Czf.Domain.NewsBankWrapper.Enum;
using System.Threading;
using System.Threading.Tasks;
using ArchiveBot.Objects;
using Czf.Domain.NewsBankWrapper.Interfaces;
using ArchiveBot.Objects.NewsBankDependancies.ignore;
using ArchiveBot.Objects.NewsBankDependancies;
using Microsoft.WindowsAzure.Storage.Table.Queryable;

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
        public static async Task Run([TimerTrigger("0 0 05 * * *")]TimerInfo myTimer, TraceWriter log)
        {
            NewsBankClient newsBankClient = new NewsBankClient(
                new EnvironmentVariableEZProxySignInUriProvider(),
                _credProvider,
                new EnvironmentVariableProductBaseUriProvider(),
                new BasicCanLog(log)
                );


            CloudTable articleTable = CloudStorageAccount
                .Parse(_storage)
                .CreateCloudTableClient()
                .GetTableReference("article");

            if (checkTableExists)
            {
                
                articleTable.CreateIfNotExists();
            }
            
            DateTime today = new DateTime(DateTime.Today.Ticks, DateTimeKind.Utc);
            TableQuery<ArticlePost> articlesPublishedBeforeToday = articleTable.CreateQuery<ArticlePost>().Where(x => x.ArticleDate < today).AsTableQuery();




            CloudTable oauthTable = CloudStorageAccount
                .Parse(_storage)
                .CreateCloudTableClient()
                .GetTableReference("oauth");

            if (checkTableExists)
            {
                checkTableExists = false;
                oauthTable.CreateIfNotExists();
            }





            RedditOAuth result = (RedditOAuth)oauthTable
                .Execute(
                    TableOperation.Retrieve<RedditOAuth>("reddit", _user)
                ).Result;
            //https://blog.maartenballiauw.be/post/2012/10/08/what-partitionkey-and-rowkey-are-for-in-windows-azure-table-storage.html
            //https://www.red-gate.com/simple-talk/cloud/cloud-data/an-introduction-to-windows-azure-table-storage/

            if (result?.GetNewToken < DateTimeOffset.Now)
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

            List<Task> updateCommentTasks = new List<Task>();
            
            foreach(ArticlePost ap in articlesPublishedBeforeToday)
            {
                Task updateComment = GetCommentLine(ap, log, newsBankClient)
                    .ContinueWith((commentLine) => 
                    {
                        Comment c = r.GetComment(new Uri("https://www.reddit.com" + ap.CommentUri));
                        EditComment(commentLine.Result, c);
                        articleTable.Execute(TableOperation.Delete(ap));
                    }
                    ,TaskContinuationOptions.OnlyOnRanToCompletion);

                updateCommentTasks.Add(updateComment);
            }
                        
            await Task.WhenAll(updateCommentTasks.ToArray());
            log.Info("AwaitWhenALL " + updateCommentTasks.Count.ToString());
        }

        static EditForNewsbank()
        {
            _storage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
            _user = Environment.GetEnvironmentVariable("BotName");
            _pass = Environment.GetEnvironmentVariable("BotPass");
            _secret = Environment.GetEnvironmentVariable("BotSecret");
            _clientId = Environment.GetEnvironmentVariable("botClientId");

            

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

        internal static async Task<string> GetCommentLine(ArticlePost articlePost, TraceWriter log, NewsBankClient newsBankClient)
        {
            log.Info("GetCommentLine");          
            
            SearchResult searchResult = null;
            try
            {
                searchResult = await newsBankClient.Search(
                        new SearchRequest()
                        {
                            Product = Product.WorldNews,
                            Publications = new List<Publication>() { Publication.SeattleTimesWebEditionArticles },
                            SearchParameter0 = new SearchParameter() { Field = SearchField.Author, Value = articlePost.ArticleAuthor.Replace("/",string.Empty) },
                            SearchParameter1 = new SearchParameter() { Field = SearchField.Headline, Value = articlePost.ArticleHeadline, ParameterCompoundOperator = CompoundOperator.AND },
                            SearchParameter2 = new SearchParameter() { Field = SearchField.Date, Value = articlePost.ArticleDate.ToShortDateString(), ParameterCompoundOperator = CompoundOperator.AND }
                        });
            }
            catch (NullReferenceException nullRefEx)  //not the best option.
            {
                log.Error("possible no Web edition result, " + articlePost.CommentUri);
                log.Error(nullRefEx.Message);
                log.Error(nullRefEx.StackTrace);
                return string.Empty;
            }
            catch(Exception e)
            {
                log.Error(e.Message);
                throw;
            }
            return $"[NewsBank version]({searchResult.FirstSearchResultItem.ResultItemUri}) via SPL [^(SPL) ^(account) ^(required)](https://www.spl.org/using-the-library/get-started/get-started-with-a-library-card/library-card-application)";
        }

        internal static void EditComment(string commentLine, Comment comment)
        {
            comment.EditText(comment.Body.Replace(":0:", commentLine));//[NewsBank version via SPL]({""})^[SPL account required]()
            comment.Save();
        }
    }
}
