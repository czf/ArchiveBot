﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net;
using HtmlAgilityPack;
namespace ArchiveBot.Core.Objects
{
    public class SeattleTimesArticle
    {
        public string Headline { get; private set; }
        public IReadOnlyList<string> ByLineAuthors{ get; private set; }
        public DateTime PublishDate { get; private set; }

        public SeattleTimesArticle(HttpResponseMessage? articleMessage)
        {
            if(articleMessage == null)
            {
                throw new ArgumentNullException(nameof(articleMessage));
            }
            Task<string> content = articleMessage.Content.ReadAsStringAsync();
            
            HtmlDocument articleDocument = new HtmlDocument();
            articleDocument.LoadHtml(content.GetAwaiter().GetResult());


            Headline = WebUtility.HtmlDecode(
                articleDocument.DocumentNode.SelectSingleNode("/html/head/meta[@itemprop='name']").GetAttributeValue("content", null));
            PublishDate = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.Parse(
                    articleDocument.DocumentNode.SelectSingleNode("/html/head/meta[@itemprop='datePublished']").GetAttributeValue("content", null)));
            HtmlNodeCollection authorNodesText = articleDocument.GetElementbyId("content").SelectNodes("//*[@class='article-byline']//a[@rel='author']//text()");
            

            ByLineAuthors = authorNodesText.Select(x => x.InnerText).ToList().AsReadOnly();
        }
    }
}
