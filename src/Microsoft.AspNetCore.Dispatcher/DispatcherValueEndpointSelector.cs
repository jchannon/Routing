// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.Dispatcher
{
    public class DispatcherValueEndpointSelector : EndpointSelector
    {
        private static readonly IReadOnlyList<SimpleEndpoint> EmptyEndpoints = Array.Empty<SimpleEndpoint>();

        private EndpointSelectorContext _endpointSelectorContext;
        private Cache _cache;

        private Cache Current
        {
            get
            {
                var endpoints = _endpointSelectorContext.Endpoints;
                var cache = Volatile.Read(ref _cache);

                if (cache != null && cache.Version == endpoints.Version)
                {
                    return cache;
                }

                cache = new Cache(endpoints);
                Volatile.Write(ref _cache, cache);
                return cache;
            }
        }

        public override Task SelectAsync(EndpointSelectorContext endpointSelectorContext)
        {
            if (endpointSelectorContext == null)
            {
                throw new ArgumentNullException(nameof(endpointSelectorContext));
            }

            _endpointSelectorContext = endpointSelectorContext;
            var dispatcherFeature = _endpointSelectorContext.HttpContext.Features.Get<IDispatcherFeature>();

            var candidates = SelectCandidates(dispatcherFeature.Values);
            var selectedEndpoint = SelectBestCandidate(candidates);
            if (selectedEndpoint == null)
            {
                // To do: Add logger
                //_logger.NoActionsMatched(context.RouteData.Values);
                return Task.CompletedTask;
            }
            _endpointSelectorContext.Endpoints.Items.Clear();
            _endpointSelectorContext.Endpoints.Items.Add(selectedEndpoint);
            return _endpointSelectorContext.InvokeNextAsync();
        }

        public IReadOnlyList<Endpoint> SelectCandidates(DispatcherValueCollection dispatcherValueCollection)
        {
            var cache = Current;

            // The Cache works based on a string[] of the route values in a pre-calculated order. This code extracts
            // those values in the correct order.
            var keys = cache.RouteKeys;
            var values = new string[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                dispatcherValueCollection.TryGetValue(keys[i], out object value);

                if (value != null)
                {
                    values[i] = value as string ?? Convert.ToString(value);
                }
            }

            if (cache.OrdinalEntries.TryGetValue(values, out var matchingRouteValues) ||
                cache.OrdinalIgnoreCaseEntries.TryGetValue(values, out matchingRouteValues))
            {
                Debug.Assert(matchingRouteValues != null);
                return matchingRouteValues;
            }

            // To do: Add logger
            //_logger.NoActionsMatched(_endpointSelectorContext.RouteData.Values);
            return EmptyEndpoints;
        }

        public Endpoint SelectBestCandidate(IReadOnlyList<Endpoint> matches)
        {
            if (matches == null || matches.Count == 0)
            {
                return null;
            }
            else if (matches.Count == 1)
            {
                var selectedEndpoint = matches[0];

                return selectedEndpoint;
            }
            else
            {
                var endpointNames = string.Join(
                    Environment.NewLine,
                    matches.Select(a => a.DisplayName));

                // To do: Add logger
                //_logger.AmbiguousActions(endpointNames);

                // To do : Throw exception
                //var message = Resources.FormatDefaultActionSelector_AmbiguousActions(
                //    Environment.NewLine,
                //    endpointNames);

                //throw new AmbiguousActionException(message);
                throw new Exception();
            }
        }

        private class Cache
        {
            public Cache(IEndpointCollection endpoints)
            {
                // We need to store the version so the cache can be invalidated if the endpoints change.
                Version = endpoints.Version;

                // We need to build two maps for all of the route values.
                OrdinalEntries = new Dictionary<string[], List<SimpleEndpoint>>(StringArrayComparer.Ordinal);
                OrdinalIgnoreCaseEntries = new Dictionary<string[], List<SimpleEndpoint>>(StringArrayComparer.OrdinalIgnoreCase);

                // We need to first identify of the keys that action selection will look at (in route data). 
                // We want to only consider conventionally routed actions here.
                var routeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < endpoints.Items.Count; i++)
                {
                    var endpoint = endpoints.Items[i] as IDispatcherValueSelectableEndpoint;

                    // This is a conventionally routed endpoint - so make sure we include its keys in the set of
                    // known route value keys.
                    foreach (var kvp in endpoint.Values)
                    {
                        routeKeys.Add(kvp.Key);
                    }
                }

                // We need to hold on to an ordered set of keys for the route values. We'll use these later to
                // extract the set of route values from an incoming request to compare against our maps of known
                // route values.
                RouteKeys = routeKeys.ToArray();

                for (var i = 0; i < endpoints.Items.Count; i++)
                {
                    var endpoint = endpoints.Items[i] as SimpleEndpoint;

                    // This is a conventionally routed endpoint - so we need to extract the route values associated
                    // with this endpoint (in order) so we can store them in our dictionaries.
                    var routeValues = new string[RouteKeys.Length];
                    for (var j = 0; j < RouteKeys.Length; j++)
                    {
                        endpoint.Values.TryGetValue(RouteKeys[j], out var value);
                        if (value != null)
                        {
                            routeValues[j] = value as string ?? Convert.ToString(value);
                        }
                    }

                    if (!OrdinalIgnoreCaseEntries.TryGetValue(routeValues, out var entries))
                    {
                        entries = new List<SimpleEndpoint>();
                        OrdinalIgnoreCaseEntries.Add(routeValues, entries);
                    }

                    entries.Add(endpoint);

                    // We also want to add the same (as in reference equality) list of endpoints to the ordinal entries.
                    // We'll keep updating `entries` to include all of the endpoints in the same equivalence class -
                    // meaning, all conventionally routed endpoints for which the route values are equal ignoring case.
                    //
                    // `entries` will appear in `OrdinalIgnoreCaseEntries` exactly once and in `OrdinalEntries` once
                    // for each variation of casing that we've seen.
                    if (!OrdinalEntries.ContainsKey(routeValues))
                    {
                        OrdinalEntries.Add(routeValues, entries);
                    }
                }
            }

            public int Version { get; }

            public string[] RouteKeys { get; }

            public Dictionary<string[], List<SimpleEndpoint>> OrdinalEntries { get; }

            public Dictionary<string[], List<SimpleEndpoint>> OrdinalIgnoreCaseEntries { get; }
        }

        private class StringArrayComparer : IEqualityComparer<string[]>
        {
            public static readonly StringArrayComparer Ordinal = new StringArrayComparer(StringComparer.Ordinal);

            public static readonly StringArrayComparer OrdinalIgnoreCase = new StringArrayComparer(StringComparer.OrdinalIgnoreCase);

            private readonly StringComparer _valueComparer;

            private StringArrayComparer(StringComparer valueComparer)
            {
                _valueComparer = valueComparer;
            }

            public bool Equals(string[] x, string[] y)
            {
                if (object.ReferenceEquals(x, y))
                {
                    return true;
                }

                if (x == null ^ y == null)
                {
                    return false;
                }

                if (x.Length != y.Length)
                {
                    return false;
                }

                for (var i = 0; i < x.Length; i++)
                {
                    if (string.IsNullOrEmpty(x[i]) && string.IsNullOrEmpty(y[i]))
                    {
                        continue;
                    }

                    if (!_valueComparer.Equals(x[i], y[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public int GetHashCode(string[] obj)
            {
                if (obj == null)
                {
                    return 0;
                }

                var hash = new HashCodeCombiner();
                for (var i = 0; i < obj.Length; i++)
                {
                    hash.Add(obj[i], _valueComparer);
                }

                return hash.CombinedHash;
            }
        }
    }
}
