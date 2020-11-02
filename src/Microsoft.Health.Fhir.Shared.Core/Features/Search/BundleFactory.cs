// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class BundleFactory : IBundleFactory
    {
        private readonly IUrlResolver _urlResolver;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly IResourceDeserializer _deserializer;

        public BundleFactory(IUrlResolver urlResolver, IFhirRequestContextAccessor fhirRequestContextAccessor, IModelInfoProvider modelInfoProvider, IResourceDeserializer deserializer)
        {
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

            _urlResolver = urlResolver;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _modelInfoProvider = modelInfoProvider;
            _deserializer = deserializer;
        }

        public ResourceElement CreateSearchBundle(SearchResult result)
        {
            return CreateBundle(result, Bundle.BundleType.Searchset, r =>
            {
                var resource = new Bundle.EntryComponent();

                resource.Resource = _deserializer.Deserialize(r.Resource).ToPoco();
                resource.FullUrlElement = new FhirUri(_urlResolver.ResolveResourceWrapperUrl(r.Resource));
                resource.Search = new Bundle.SearchComponent
                {
                    Mode = r.SearchEntryMode == SearchEntryMode.Match ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include,
                };

                return resource;
            });
        }

        public ResourceElement CreateHistoryBundle(SearchResult result)
        {
            return CreateBundle(result, Bundle.BundleType.History, r =>
            {
                var resource = new RawBundleEntryComponent(r.Resource);
                var hasVerb = Enum.TryParse(r.Resource.Request?.Method, true, out Bundle.HTTPVerb httpVerb);

                resource.FullUrlElement = new FhirUri(_urlResolver.ResolveResourceWrapperUrl(r.Resource, true));
                resource.Request = new Bundle.RequestComponent
                {
                    Method = hasVerb ? (Bundle.HTTPVerb?)httpVerb : null,
                    Url = hasVerb ? $"{r.Resource.ResourceTypeName}/{(httpVerb == Bundle.HTTPVerb.POST ? null : r.Resource.ResourceId)}" : null,
                };
                resource.Response = new Bundle.ResponseComponent
                {
                    LastModified = r.Resource.LastModified,
                    Etag = WeakETag.FromVersionId(r.Resource.Version).ToString(),
                };

                return resource;
            });
        }

        private ResourceElement CreateBundle(SearchResult result, Bundle.BundleType type, Func<SearchResultEntry, Bundle.EntryComponent> selectionFunction)
        {
            EnsureArg.IsNotNull(result, nameof(result));

            return CreateBundle(
                type.ToString(),
                result.Results.Select(selectionFunction).ToArray(),
                result.UnsupportedSearchParameters,
                result.UnsupportedSortingParameters,
                result.ContinuationToken,
                result.TotalCount,
                result.Partial);
        }

        public object ConvertToPocoEntryComponent(ITypedElement entryComponent)
        {
            Bundle.SearchEntryMode? mode = Enum.TryParse(entryComponent.Scalar("search.mode")?.ToString(), out Bundle.SearchEntryMode parsedMode) ? parsedMode : (Bundle.SearchEntryMode?)null;

            return new Bundle.EntryComponent
            {
                Resource = entryComponent.Select("resource").First().ToPoco<Resource>(),
                Search = new Bundle.SearchComponent
                {
                    Mode = mode,
                },
            };
        }

        public ResourceElement CreateBundle(
            string bundleType,
            IEnumerable<object> entries,
            IEnumerable<Tuple<string, string>> unsupportedSearchParams = null,
            IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters = null,
            string continuationToken = null,
            int? totalCount = null,
            bool? isPartial = null)
        {
            EnsureArg.IsNotNull(bundleType, nameof(bundleType));
            EnsureArg.IsNotNull(entries, nameof(entries));

            // Create the bundle from the result.
            var bundle = new Bundle();

            if (_fhirRequestContextAccessor.FhirRequestContext.BundleIssues.Any())
            {
                var operationOutcome = new OperationOutcome
                {
                    Issue = new List<OperationOutcome.IssueComponent>(_fhirRequestContextAccessor.FhirRequestContext.BundleIssues.Select(x => x.ToPoco())),
                };

                bundle.Entry.Add(new Bundle.EntryComponent
                {
                    Resource = operationOutcome,
                    Search = new Bundle.SearchComponent
                    {
                        Mode = Bundle.SearchEntryMode.Outcome,
                    },
                });

                _fhirRequestContextAccessor.FhirRequestContext.BundleIssues.Clear();
            }

            // Hacky conversion, but for proof-of-concept...
            bundle.Entry.AddRange(entries.Cast<Bundle.EntryComponent>());

            if (continuationToken != null)
            {
                bundle.NextLink = _urlResolver.ResolveRouteUrl(
                    unsupportedSearchParams,
                    unsupportedSortingParameters,
                    Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(continuationToken)),
                    true);
            }

            if (isPartial.GetValueOrDefault())
            {
                // if the query resulted in a partial indication, add appropriate outcome
                // as an entry
                var resource = new OperationOutcome();
                resource.Issue = new List<OperationOutcome.IssueComponent>();
                resource.Issue.Add(new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Warning,
                    Code = OperationOutcome.IssueType.Incomplete,
                    Diagnostics = Core.Resources.TruncatedIncludeMessage,
                });

                bundle.Entry.Add(new Bundle.EntryComponent()
                {
                    Resource = resource,
                    Search = new Bundle.SearchComponent
                    {
                        Mode = Bundle.SearchEntryMode.Outcome,
                    },
                });
            }

            // Add the self link to indicate which search parameters were used.
            bundle.SelfLink = _urlResolver.ResolveRouteUrl(unsupportedSearchParams, unsupportedSortingParameters);

            bundle.Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId;
            bundle.Type = Enum.Parse<Bundle.BundleType>(bundleType);
            bundle.Total = totalCount;
            bundle.Meta = new Meta
            {
                LastUpdated = Clock.UtcNow,
            };

            return bundle.ToResourceElement();
        }

        public object CreateIncludedEntryComponent(ITypedElement resource)
        {
            return new Bundle.EntryComponent
            {
                Resource = resource.ToPoco<Resource>(),
                Search = new Bundle.SearchComponent
                {
                    Mode = Bundle.SearchEntryMode.Include,
                },
            };
        }
    }
}
