using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace AZSHFuncs {

    public class MarketPlaceItem {
        public DateTime ReleaseDate;
        public string Name;
        public string Change;
    }

    public static class CreateAZSHMarketPlaceFeed {
        private static HttpClient httpClient = new HttpClient();
        private static string marketPlaceUpdatesURL = System.Environment.GetEnvironmentVariable("MarketPlaceUpdatesURL");
        private static DateTime lastUpdated;
        private const string lastUpdatedMetadataKey = "GitHubPageUpdate";
        private static Regex sectionRegex = new Regex(@"## (\w+) marketplace items", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static Regex itemRegex = new Regex(@"- (?<date>\d{2}\/\d{2}\/\d{4}): (?<product>.+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        [FunctionName("CreateAZSHMarketPlaceFeed")]
        public static async Task Run([TimerTrigger("%ScheduleTriggerTime%")] TimerInfo timer, [Blob("%RSSPath%", FileAccess.Write, Connection = "StorageConnection")] CloudBlockBlob feedBlob,
            ILogger log) {

            log.LogInformation($"AzureStack Hun MarketPlace RSS Generator launched at: {DateTime.Now}");

            List<MarketPlaceItem> marketPlaceItems = new List<MarketPlaceItem>();

            try {
                var stream = await httpClient.GetStreamAsync(marketPlaceUpdatesURL);
                using(StreamReader reader = new StreamReader(stream)) {
                    string line;
                    string section = "";
                    while (null != (line = await reader.ReadLineAsync())) {

                        if (line.Contains("ms.date:")) {
                            lastUpdated = DateTime.Parse(line.Split(":") [1].Trim(), System.Globalization.CultureInfo.GetCultureInfo("en-US").DateTimeFormat,System.Globalization.DateTimeStyles.AssumeUniversal);
                            log.LogInformation($"GitHub page was last updated on: {lastUpdated.ToShortDateString()}");
                            if (feedBlob.Exists() &&
                                feedBlob.Metadata.ContainsKey(lastUpdatedMetadataKey) &&
                                feedBlob.Metadata[lastUpdatedMetadataKey].Equals(lastUpdated.ToShortDateString())) {

                                log.LogInformation("RSS Feed is already up to date, will quit");
                                return;

                            }
                        }

                        var matchSection = sectionRegex.Match(line);
                        if (matchSection.Success) {
                            section = matchSection.Groups[1].Value;
                            continue;
                        }

                        var matchItem = itemRegex.Match(line);
                        if (matchItem.Success) {
                            marketPlaceItems.Add(
                                new MarketPlaceItem {
                                    Name = matchItem.Groups["product"].Value,
                                        Change = section,
                                        ReleaseDate = DateTime.Parse(matchItem.Groups["date"].Value, System.Globalization.CultureInfo.GetCultureInfo("en-US").DateTimeFormat,System.Globalization.DateTimeStyles.AssumeUniversal)

                                }
                            );
                        }
                    }
                }
            } catch (Exception e) {
                log.LogError($"Error while fetch Marketplace Items page: {e.Message}");
                return;
            }

            if (marketPlaceItems.Count > 0) {
                log.LogInformation($"Got {marketPlaceItems.Count} items");
                var feed = FormatRssFeed(marketPlaceItems);

                var settings = new XmlWriterSettings {
                    Encoding = System.Text.Encoding.UTF8,
                    NewLineHandling = NewLineHandling.Entitize,
                    NewLineOnAttributes = true,
                    Indent = true,
                    Async = true
                };

                using(var stream = new MemoryStream()) {
                    using(var xmlWriter = XmlWriter.Create(stream, settings)) {
                        var rssFormatter = new Rss20FeedFormatter(feed, false);
                        rssFormatter.WriteTo(xmlWriter);
                        await xmlWriter.FlushAsync();
                    }
                    stream.Seek(0,SeekOrigin.Begin);
                    feedBlob.Properties.ContentType = "application/rss+xml; charset=utf-8";
                    feedBlob.Metadata[lastUpdatedMetadataKey] = lastUpdated.ToShortDateString();
                    await feedBlob.UploadFromStreamAsync(stream);
                }
            }

        }

        private static SyndicationFeed FormatRssFeed(List<MarketPlaceItem> marketPlaceItems) {
            var feed = new SyndicationFeed {
                Title = new TextSyndicationContent("Azure Stack Hub Market Place Updates"),
                Description = new TextSyndicationContent("Provide latest updates about Azure Stack Hub marketplace"),
                LastUpdatedTime = new DateTimeOffset(lastUpdated.ToUniversalTime())

            };
            feed.Links.Add(new SyndicationLink(new Uri(marketPlaceUpdatesURL), "alternate", "Azure Stack Hub Market Place Changelog", "text/html", 1000));

            var feedItems = marketPlaceItems.GroupBy(g => new { g.ReleaseDate })
                .OrderByDescending(x => x.Key.ReleaseDate)
                .Select(i => new SyndicationItem {
                    Title = new TextSyndicationContent("Market Place Item Update on " + i.Key.ReleaseDate.ToShortDateString()),
                        PublishDate = new DateTimeOffset(i.Key.ReleaseDate.ToUniversalTime()),
                        Content = new TextSyndicationContent(String.Join("\n", i.ToList().Select(it => String.Format("{0} - {1}", it.Change, it.Name))),TextSyndicationContentKind.Plaintext)
                });

            feed.Items = feedItems;
            return feed;
        }
    }
}