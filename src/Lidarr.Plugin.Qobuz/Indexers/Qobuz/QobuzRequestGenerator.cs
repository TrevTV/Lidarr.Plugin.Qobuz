using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Plugin.Qobuz.API;

namespace NzbDrone.Core.Indexers.Qobuz
{
    public class QobuzRequestGenerator : IIndexerRequestGenerator
    {
        private const int PageSize = 100;
        private const int MaxPages = 30;
        public QobuzIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            // this is a lazy implementation, just here so that lidarr has something to test against when saving settings
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequests("never gonna give you up"));

            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AlbumSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            chain.AddTier(GetRequests($"{searchCriteria.ArtistQuery} {searchCriteria.AlbumQuery}"));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            chain.AddTier(GetRequests(searchCriteria.ArtistQuery));

            return chain;
        }

        private IEnumerable<IndexerRequest> GetRequests(string searchParameters)
        {
            // make sure we are logged in and have valid credentials
            // if we don't it should throw an error
            if (!QobuzAPI.Instance.Client.IsAppSecretValid())
            {
                QobuzAPI.Instance.PickSignInFromSettings(Settings, Logger);
            }

            // TODO: search
            return [];
            /*for (var page = 0; page < MaxPages; page++)
            {
                var data = new Dictionary<string, string>()
                {
                    ["query"] = searchParameters,
                    ["limit"] = $"{PageSize}",
                    ["types"] = "albums,tracks",
                    ["offset"] = $"{page * PageSize}",
                };

                var url = QobuzAPI.Instance!.GetAPIUrl("search", data);
                var req = new IndexerRequest(url, HttpAccept.Json);
                req.HttpRequest.Method = System.Net.Http.HttpMethod.Get;
                req.HttpRequest.Headers.Add("Authorization", $"{QobuzAPI.Instance.Client.ActiveUser.TokenType} {QobuzAPI.Instance.Client.ActiveUser.AccessToken}");
                yield return req;
            }*/
        }
    }
}
