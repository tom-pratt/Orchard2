﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Orchard.ContentManagement;
using Orchard.ContentManagement.Records;
using Orchard.Lucene.Services;
using Orchard.Queries;
using Orchard.Tokens.Services;
using YesSql;
using YesSql.Services;

namespace Orchard.Lucene
{
    public class LuceneQuerySource : IQuerySource
    {
        private readonly LuceneIndexManager _luceneIndexProvider;
        private readonly LuceneIndexingService _luceneIndexingService;
        private readonly LuceneAnalyzerManager _luceneAnalyzerManager;
        private readonly ILuceneQueryService _queryService;
        private readonly ITokenizer _tokenizer;
        private readonly ISession _session;

        public LuceneQuerySource(
            LuceneIndexManager luceneIndexProvider,
            LuceneIndexingService luceneIndexingService,
            LuceneAnalyzerManager luceneAnalyzerManager,
            ILuceneQueryService queryService,
            ITokenizer tokenizer,
            ISession session)
        {
            _luceneIndexProvider = luceneIndexProvider;
            _luceneIndexingService = luceneIndexingService;
            _luceneAnalyzerManager = luceneAnalyzerManager;
            _queryService = queryService;
            _tokenizer = tokenizer;
            _session = session;
        }

        public string Name => "Lucene";

        public Query Create()
        {
            return new LuceneQuery();
        }

        public async Task<object> ExecuteQueryAsync(Query query, IDictionary<string, object> parameters)
        {
            var luceneQuery = query as LuceneQuery;
            object result = null;

            await _luceneIndexProvider.SearchAsync (luceneQuery.Index, async searcher =>
            {
                var tokenizedContent = _tokenizer.Tokenize(luceneQuery.Template, parameters);
                var parameterizedQuery = JObject.Parse(tokenizedContent);

                var analyzer = _luceneAnalyzerManager.CreateAnalyzer("standardanalyzer");
                var context = new LuceneQueryContext(searcher, LuceneSettings.DefaultVersion, analyzer);
                var docs = await _queryService.SearchAsync(context, parameterizedQuery);

                if (luceneQuery.ReturnContentItems)
                {
                    // Load corresponding content item versions
                    var contentItemVersionIds = docs.ScoreDocs.Select(x => searcher.Doc(x.Doc).Get("Content.ContentItem.ContentItemVersionId")).ToArray();
                    var contentItems = await _session.Query<ContentItem, ContentItemIndex>(x => x.ContentItemVersionId.IsIn(contentItemVersionIds)).ListAsync();

                    // Reorder the result to preserve the one from the lucene query
                    var indexed = contentItems.ToDictionary(x => x.ContentItemVersionId, x => x);
                    result = contentItemVersionIds.Select(x => indexed[x]).ToArray();
                }
                else
                {
                    var results = new List<JObject>();
                    foreach (var document in docs.ScoreDocs.Select(hit => searcher.Doc(hit.Doc)))
                    {
                        results.Add(new JObject(document.Select(x => new JProperty(x.Name, x.GetStringValue()))));
                    }

                    result = results;
                }
            });

            return result;
        }
    }
}
