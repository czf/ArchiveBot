using ArchiveBot.Core.Objects;
using Azure;
using Azure.Data.Tables;
using RedditSharp.Things;

namespace ArchiveBot.Core
{
    public class ArticlePost : ITableEntity
    {
        public string? ArticleAuthor { get; set; }
        public string? ArticleHeadline { get; set; }
        public DateTime ArticleDate { get; set; }
        public string? CommentUri { get; set; }
        public string? PartitionKey { get; set; }
        public string? RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public ArticlePost() { }
        public ArticlePost(SeattleTimesArticle seattleTimesArticle, Comment? comment)
        {
            ArticleAuthor = seattleTimesArticle.ByLineAuthors.First();
            ArticleHeadline = seattleTimesArticle.Headline;
            ArticleDate = seattleTimesArticle.PublishDate;
            CommentUri = comment?.Permalink?.ToString();
            PartitionKey = comment?.Subreddit;
            RowKey = comment?.Id;

        }
    }
}
