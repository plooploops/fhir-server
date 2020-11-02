// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class SearchResourceResponse
    {
        public SearchResourceResponse(
            ResourceElement bundle,
            IEnumerable<Tuple<string, string>> unsupportedSearchParams = null,
            IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters = null,
            string continuationToken = null,
            int? totalCount = null)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            Bundle = bundle;
            UnsupportedSearchParams = unsupportedSearchParams;
            UnsupportedSortingParameters = unsupportedSortingParameters;
            ContinuationToken = continuationToken;
            TotalCount = totalCount;
        }

        public ResourceElement Bundle { get; }

        public IEnumerable<Tuple<string, string>> UnsupportedSearchParams { get; }

        public IReadOnlyList<(string parameterName, string reason)> UnsupportedSortingParameters { get; }

        public string ContinuationToken { get; }

        public int? TotalCount { get; }
    }
}
