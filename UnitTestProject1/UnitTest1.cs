using System;
using System.Net.Http;
using System.Net.Http.Headers;
using ArchiveBot.Core.Objects;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestMethod1()
        {
            SeattleTimesArticle article = null;
            using HttpClient client = new HttpClient();
            


            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/104.0.5112.102 Safari/537.36");
            client.DefaultRequestHeaders.Add("Connection","keep-alive");
            client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");

            using HttpResponseMessage message = client.GetAsync(
                new Uri("https://www.seattletimes.com/business/retail/dying-fad-fewer-stores-are-staying-open-on-thanksgiving-day-as-online-shopping-takes-off/")).Result;


            article = new SeattleTimesArticle(message);
            

            Assert.IsNotNull(article);
            Assert.IsNotNull(article.Headline);
            Assert.IsNotNull(article.ByLineAuthors);
        }
        public void TestMethod2()
        {
            SeattleTimesArticle article = null;
            using (HttpClient client = new HttpClient())
            using (HttpResponseMessage message = client.GetAsync(new Uri("https://www.seattletimes.com/life/outdoors/starter-kit-what-you-need-to-survive-seattles-rain/")).Result)
            {
                article = new SeattleTimesArticle(message);
            }

            Assert.IsNotNull(article);
            Assert.IsNotNull(article.Headline);
            Assert.IsNotNull(article.ByLineAuthors);
        }
    }
}
