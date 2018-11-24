using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Web;

using System.Threading.Tasks;
using HtmlAgilityPack;
namespace ArchiveBot.Objects
{
    public class SeattleTimesArticle
    {
        public string Headline { get; private set; }
        public IReadOnlyList<string> ByLineAuthors{ get; private set; }
        public DateTime PublishDate { get; private set; }

        public SeattleTimesArticle(HttpResponseMessage articleMessage)
        {
            Task<string> content = articleMessage.Content.ReadAsStringAsync();
            
            HtmlDocument articleDocument = new HtmlDocument();
            articleDocument.LoadHtml(content.GetAwaiter().GetResult());


            Headline = HttpUtility.HtmlDecode(
                articleDocument.DocumentNode.SelectSingleNode("/html/head/meta[@itemprop='name']").GetAttributeValue("content", null));
            PublishDate  =
                DateTime.Parse( articleDocument.DocumentNode.SelectSingleNode("/html/head/meta[@itemprop='datePublished']").GetAttributeValue("content", null));
            HtmlNodeCollection authorNodesText = articleDocument.GetElementbyId("content").SelectNodes("//*[@class='article-byline']//a[@rel='author']/text()");


            ByLineAuthors = authorNodesText.Select(x => x.InnerText).ToList().AsReadOnly();
        }
    }
}
