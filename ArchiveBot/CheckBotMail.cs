using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;
using System;
using System.Net;
using System.Net.Http;
using System.Linq;

using RedditSharp;
using RedditSharp.Things;
using System.Security.Authentication;

namespace ArchiveBot
{
    public static class CheckBotMail
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

        [FunctionName("CheckBotMail")]
        public static HttpResponseMessage Run([HttpTrigger(AuthorizationLevel.Anonymous,  "post", Route = "CheckBotMail/name/{name}")]HttpRequestMessage req, string name, TraceWriter log)
        {
            string storage = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            string user = name;
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

            oauthTable
                .Execute(
                    TableOperation.InsertOrReplace(result));


            foreach (Thing t in r.User.UnreadMessages.Where(x=> x is Comment))
            {
                Comment c = t as Comment;
                string replyMsg = "[Yay!](https://www.youtube.com/watch?v=Y42F9lyIyp4)";
                if (!Debug)
                {
                    c.SetAsRead();
                    if (string.Equals(c.Body.ToLower().Trim(), "good bot"))
                    {
                        c.Reply(replyMsg);
                    }
                    else
                    {
                        log.Info("unknown reply: " + c.Body);
                    }

                }
                else
                {
                    if (string.Equals(c.Body.ToLower().Trim(), "good bot"))
                    {
                        log.Info(replyMsg);
                    }
                    else
                    {
                        log.Info("unknown reply: " + c.Body);
                    }
                }
            }



            log.Info("C# HTTP trigger function processed a request.");

            // Fetching the name from the path parameter in the request URL
            return req.CreateResponse(HttpStatusCode.OK);
        }
    }
}
