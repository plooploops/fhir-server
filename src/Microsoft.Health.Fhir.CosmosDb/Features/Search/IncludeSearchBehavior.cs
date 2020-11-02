// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    public class IncludeSearchBehavior : IPipelineBehavior<SearchResourceRequest, SearchResourceResponse>
    {
        private readonly ISearchService _searchService;
        private readonly ISearchIndexer _indexer;
        private readonly IResourceDeserializer _deserializer;
        private readonly IBundleFactory _bundleFactory;
        private readonly ISearchParameterDefinitionManager _spdm;

        public IncludeSearchBehavior(
            ISearchService searchService,
            ISearchIndexer indexer,
            IResourceDeserializer deserializer,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver spdm,
            IBundleFactory bundleFactory)
        {
            _searchService = searchService;
            _indexer = indexer;
            _deserializer = deserializer;
            _bundleFactory = bundleFactory;
            _spdm = spdm();
        }

        public async Task<SearchResourceResponse> Handle(SearchResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<SearchResourceResponse> next)
        {
            var includes = request.Queries.Where(x => string.Equals("_include", x.Item1, StringComparison.Ordinal)).ToList();
            request.Queries = request.Queries.Except(includes).ToList();

            SearchResourceResponse result = await next();

            if (includes.Any())
            {
                var includedResources = new List<ResourceWrapper>();
                var bundleWrapper = new BundleWrapper(result.Bundle.Instance);
                var searchParamList = new List<(string ResourceType, string ParamName, string Value)>();

                // Only include for "matched" resourced
                foreach (BundleEntryWrapper entry in bundleWrapper.Entries.Where(x => x.IsMatch))
                {
                    var extracted = _indexer.Extract(entry.Resource.ToResourceElement());

                    foreach (var include in includes)
                    {
                        var fragments = include.Item2.Split(":", StringSplitOptions.RemoveEmptyEntries);
                        var includeType = fragments[0];
                        var targetType = fragments.Length == 3 ? fragments[2] : null;

                        if (!string.Equals(entry.Resource.InstanceType, includeType, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Extract the include references from the matched resources
                        var paramName = fragments[1];
                        var searchParam = _spdm.GetSearchParameter(includeType, paramName);
                        var values = extracted.Where(x => x.SearchParameter == searchParam).Where(x => x.Value is ReferenceSearchValue);

                        foreach (var item in values)
                        {
                            searchParamList.Add((targetType ?? searchParam.TargetResourceTypes.Single(), "_id", ((ReferenceSearchValue)item.Value).ResourceId));
                        }
                    }
                }

                // Lookup the referenced resources by ID
                foreach (var grouping in searchParamList.GroupBy(x => $"{x.ResourceType}_{x.ParamName}"))
                {
                    var results = await _searchService.SearchAsync(
                        grouping.First().ResourceType,
                        new[] { Tuple.Create(grouping.First().ParamName, string.Join(",", grouping.Select(x => x.Value))) },
                        cancellationToken);

                    foreach (var includeResult in results.Results)
                    {
                        includedResources.Add(includeResult.Resource);
                    }
                }

                ResourceElement newBundle = _bundleFactory.CreateBundle(
                    "Searchset",
                    bundleWrapper.Entries.Select(x => _bundleFactory.ConvertToPocoEntryComponent(x.Entry))
                        .Concat(includedResources.Select(x => _bundleFactory.CreateIncludedEntryComponent(_deserializer.Deserialize(x).Instance))),
                    result.UnsupportedSearchParams,
                    result.UnsupportedSortingParameters,
                    result.ContinuationToken,
                    result.TotalCount);

                return new SearchResourceResponse(newBundle);
            }
            else
            {
                return result;
            }
        }
    }
}
