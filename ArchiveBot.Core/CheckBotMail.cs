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
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using System.Threading.Tasks;
using ArchiveBot.Core.Objects;

namespace ArchiveBot.Core
{
    public class CheckBotMail
    {
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
                    catch (Exception e)
                    {
                        _debug = false;
                    }
                }
                return _debug.Value;
            }
        }

        private static string GoodBotReplyMsg
        {
            get
            {
                string result = null;
                switch (new Random().Next(3))
                {
                    case 0:
                        result = "[Thanks!](https://ak.picdn.net/shutterstock/videos/1056322661/preview/stock-footage-futuristic-prototype-robot-finishing-presentation-in-meeting-room-excited-audience-of-office.webm)";
                        break;
                    case 1:
                        result = "[aww yeah](https://www.youtube.com/watch?v=r3J5XfnjvwY)"; //  aww yeah
                        break;
                    case 2:
                        result = "[nice](https://www.youtube.com/watch?v=Vv_3gcLhE9w)";
                        break;
                        //https://ak.picdn.net/shutterstock/videos/1036047215/preview/stock-footage-young-woman-and-droid-dance-together-cyborg-and-human-concept.webm robot dance
                        //https://gfycat.com/ifr/WeepyPresentCub blushing robot
                        //https://gfycat.com/ifr/DimwittedFavorableKillerwhale robot hug
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

                        //https://gfycat.com/ifr/UnpleasantDetailedHoiho gfycat of robot walking fliping the bird
                }
                return result;
            }
        }

        private string _user => _botParameterCredentialsProvider.BotName;
        private string _pass => _botParameterCredentialsProvider.BotPassword;
        private string _secret => _botParameterCredentialsProvider.BotSecret;
        private string _clientId => _botParameterCredentialsProvider.BotClientId;
        private readonly IBotParameterCredentialsProvider _botParameterCredentialsProvider;
        private readonly TableServiceClient _tableServiceClient;
        private readonly ILogger _log;

        public CheckBotMail(IBotParameterCredentialsProvider botParameterCredentialsProvider,
            TableServiceClient tableServiceClient,
            ILogger log)

        {
            _botParameterCredentialsProvider = botParameterCredentialsProvider;
            _tableServiceClient = tableServiceClient;
            _log = log;

        }

        public async Task RunAsync()
        {
            //CloudTable oauthTable = CloudStorageAccount
            //   .Parse(storage)
            //   .CreateCloudTableClient()
            //   .GetTableReference("oauth");

            
            //RedditOAuth result = (RedditOAuth)oauthTable
            //    .Execute(
            //        TableOperation.Retrieve<RedditOAuth>("reddit", user)
            //    ).Result;


            var oauthTableClient = _tableServiceClient.GetTableClient("oauth");
            RedditOAuth result = await oauthTableClient.GetEntityAsync<RedditOAuth>("reddit", _user);
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
                    agent = new BotWebAgent(_user, _pass, _clientId, _secret, "https://www.reddit.com/user/somekindofbot0000/");
                    result = new RedditOAuth() { Token = agent.AccessToken, GetNewToken = DateTimeOffset.Now.AddMinutes(57), PartitionKey = "reddit", RowKey = _user };
                    r = new Reddit(agent, true);
                }
                else
                {
                    try
                    {
                        //agent = new BotWebAgent(result.RefreshToken, clientId, secret, "https://www.reddit.com/user/somekindofbot0000/");
                        r = new Reddit(result.Token);
                    }
                    catch (AuthenticationException )
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

            await r.InitOrUpdateUserAsync();
            await foreach (Thing t in r.User.GetUnreadMessages().Where(x=> x is Comment))
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



                await ReplyAction(replyMsg, c, _log, isUnknownMessage);

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

            _log.LogInformation("finished check bot mail");

            
        }

        private static async Task ReplyAction(string replyMsg, Comment comment, ILogger log, bool isUnknownMessage)
        {
            if (!Debug)
            {
               await comment.SetAsReadAsync();
            }

            if(!isUnknownMessage && !Debug)
            {
                await comment.ReplyAsync(replyMsg);
                log.LogInformation($"Replying with: {replyMsg}");
            }
            else
            {
                log.LogInformation(replyMsg);
            }

            
        }
    }
}
