// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface IBundleFactory
    {
        ResourceElement CreateSearchBundle(SearchResult result);

        ResourceElement CreateHistoryBundle(SearchResult result);

        // Hacky conversion, but for proof-of-concept...
        object CreateIncludedEntryComponent(ITypedElement resource);

        // Hacky conversion, but for proof-of-concept...
        object ConvertToPocoEntryComponent(ITypedElement entryComponent);

        // Hacky conversion, but for proof-of-concept...
        ResourceElement CreateBundle(
            string bundleType,
            IEnumerable<object> entries,
            IEnumerable<Tuple<string, string>> unsupportedSearchParams = null,
            IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters = null,
            string continuationToken = null,
            int? totalCount = null,
            bool? isPartial = null);
    }
}
