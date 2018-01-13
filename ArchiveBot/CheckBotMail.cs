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

        private static string GoodBotReplyMsg
        {
            get
            {
                string result = null;
                switch (new Random().Next(2))
                {
                    case 0:
                        result = "[Yay!](https://www.youtube.com/watch?v=Y42F9lyIyp4)";
                        break;
                    case 1:
                        result = "[Thanks](https://www.youtube.com/watch?v=hRb6ordY4J4)"; //thumbs up
                        break;

                }
                return result;

            }
        }

        private static string BadBotReplyMsg
        {
            get
            {
                string result = null;
                switch (new Random().Next(4))
                {
                    case 0:
                        result = "[^^*sad* ^^beep](https://www.youtube.com/watch?v=-_Ykc91L5kY)";//random robot screech
                        break;
                    case 1:
                        result = "[^^*sad* ^^boop](https://www.youtube.com/watch?v=SE_iOAQwgL0)";//Gasp
                        break;
                    case 2:
                        result = "[*oh yeah?*](https://www.youtube.com/watch?v=yvllQl5t4Ww)";//Your mother
                        break;
                    case 3:
                        result = "[*^^heh ^^heh...*](https://www.youtube.com/watch?v=8qJac4PR1aI)";//Sad bender laugh
                        break;
                        //https://www.youtube.com/watch?v=lIInXiCZUYE //what?
                        //https://www.youtube.com/watch?v=YqZKsmp0z6A //what?[2] loop this?
                }
                return result;
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
                    result = new RedditOAuth() { Token = agent.AccessToken, GetNewToken = DateTimeOffset.Now.AddMinutes(57), PartitionKey = "reddit", RowKey = user };
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

            //This save might be affecting the archive function
            //oauthTable
            //    .Execute(
            //        TableOperation.InsertOrReplace(result));


            foreach (Thing t in r.User.UnreadMessages.Where(x=> x is Comment))
            {
                Comment c = t as Comment;
                string cleanBody = c.Body.ToLower().Trim().Replace(".", "");
                string replyMsg = null;
                bool isUnknownMessage = false;
                if(string.Equals(cleanBody, "good bot"))
                {
                    replyMsg = GoodBotReplyMsg;
                }
                else if (string.Equals(cleanBody, "bad bot"))
                {
                    replyMsg = BadBotReplyMsg;
                }
                else
                {
                    isUnknownMessage = true;
                    replyMsg = $"unknown reply: {c.Body}";

                }



                ReplyAction(replyMsg, c, log, isUnknownMessage);

                //else
                //{
                //    if (string.Equals(cleanBody, "good bot"))
                //    {
                //        log.Info(replyMsg);
                //    }
                //    else
                //    {
                //        log.Info("unknown reply: " + c.Body);
                //    }
                //}
            }

            log.Info("C# HTTP trigger function processed a request.");

            // Fetching the name from the path parameter in the request URL
            return req.CreateResponse(HttpStatusCode.OK);
        }

        private static void ReplyAction(string replyMsg, Comment comment, TraceWriter log, bool isUnknownMessage)
        {
            if (!Debug)
            {
                comment.SetAsRead();
            }

            if(!isUnknownMessage && !Debug)
            {
                comment.Reply(replyMsg);
                log.Info($"Replying with: {replyMsg}");
            }
            else
            {
                log.Info(replyMsg);
            }

            
        }
    }
}
