﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Dispatcher;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Internal;
using Microsoft.AspNetCore.Routing.Logging;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.AspNetCore.Routing.Tree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Routing.Dispatcher
{
    public class TreeDispatcher : DispatcherBase
    {
        private bool _dataInitialized;
        private bool _servicesInitialized;
        private object _lock;
        private Cache _cache;

        private readonly Func<Cache> _initializer;

        private ILogger _logger;

        public TreeDispatcher()
        {
            _lock = new object();
            _initializer = CreateCache;
        }

        public override async Task InvokeAsync(HttpContext httpContext)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            EnsureServicesInitialized(httpContext);

            var cache = LazyInitializer.EnsureInitialized(ref _cache, ref _dataInitialized, ref _lock, _initializer);

            var feature = httpContext.Features.Get<IDispatcherFeature>();
            var values = feature.Values?.AsRouteValueDictionary() ?? new RouteValueDictionary();
            feature.Values = values;

            for (var i = 0; i < cache.Trees.Length; i++)
            {
                var tree = cache.Trees[i];
                var tokenizer = new PathTokenizer(httpContext.Request.Path);

                var treenumerator = new Treenumerator(tree.Root, tokenizer);

                while (treenumerator.MoveNext())
                {
                    var node = treenumerator.Current;
                    foreach (var item in node.Matches)
                    {
                        var entry = item.Entry;
                        var matcher = item.TemplateMatcher;
                        
                        values.Clear();
                        if (!matcher.TryMatch(httpContext.Request.Path, values))
                        {
                            continue;
                        }

                        _logger.MatchedRoute(entry.RouteName, entry.RouteTemplate.TemplateText);

                        if (!MatchConstraints(httpContext, values, entry.Constraints))
                        {
                            continue;
                        }

                        feature.Endpoint = await SelectEndpointAsync(httpContext, (Endpoint[])entry.Tag, Selectors);
                        if (feature.Endpoint != null || feature.RequestDelegate != null)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private bool MatchConstraints(HttpContext httpContext, RouteValueDictionary values, IDictionary<string, IRouteConstraint> constraints)
        {
            if (constraints != null)
            {
                foreach (var kvp in constraints)
                {
                    var constraint = kvp.Value;
                    if (!constraint.Match(httpContext, null, kvp.Key, values, RouteDirection.IncomingRequest))
                    {
                        object value;
                        values.TryGetValue(kvp.Key, out value);

                        _logger.RouteValueDoesNotMatchConstraint(value, kvp.Key, kvp.Value);
                        return false;
                    }
                }
            }

            return true;
        }

        private void EnsureServicesInitialized(HttpContext httpContext)
        {
            if (Volatile.Read(ref _servicesInitialized))
            {
                return;
            }

            EnsureServicesInitializedSlow(httpContext);
        }

        private void EnsureServicesInitializedSlow(HttpContext httpContext)
        {
            lock (_lock)
            {
                if (!Volatile.Read(ref _servicesInitialized))
                {
                    _logger = httpContext.RequestServices.GetRequiredService<ILogger<TreeDispatcher>>();
                }
            }
        }

        private Cache CreateCache()
        {
            var endpoints = GetEndpoints();

            var groups = new Dictionary<Key, List<Endpoint>>();

            for (var i = 0; i < endpoints.Count; i++)
            {
                var endpoint = endpoints[i];

                var metadata = endpoint.Metadata.OfType<ITreeDispatcherMetadata>().LastOrDefault();
                if (metadata == null)
                {
                    continue;
                }

                if (!groups.TryGetValue(new Key(metadata.Order, metadata.RouteTemplate), out var group))
                {
                    group = new List<Endpoint>();
                    groups.Add(new Key(metadata.Order, metadata.RouteTemplate), group);
                }

                group.Add(endpoint);
            }

            var entries = new List<InboundRouteEntry>();
            foreach (var group in groups)
            {
                var template = TemplateParser.Parse(group.Key.RouteTemplate);

                var defaults = new RouteValueDictionary();
                for (var i = 0; i < template.Parameters.Count; i++)
                {
                    var parameter = template.Parameters[i];
                    if (parameter.DefaultValue != null)
                    {
                        defaults.Add(parameter.Name, parameter.DefaultValue);
                    }
                }

                entries.Add(new InboundRouteEntry()
                {
                    Defaults = defaults,
                    Order = group.Key.Order,
                    Precedence = RoutePrecedence.ComputeInbound(template),
                    RouteTemplate = template,
                    Tag = group.Value.ToArray(),
                });
            }

            var trees = new List<UrlMatchingTree>();
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];

                while (trees.Count <= entry.Order)
                {
                    trees.Add(new UrlMatchingTree(trees.Count));
                }

                var tree = trees[i];

                TreeRouteBuilder.AddEntryToTree(tree, entry);
            }

            return new Cache(trees.ToArray());
        }

        private struct Key : IEquatable<Key>
        {
            public readonly int Order;
            public readonly string RouteTemplate;

            public Key(int order, string routeTemplate)
            {
                Order = order;
                RouteTemplate = routeTemplate;
            }

            public bool Equals(Key other)
            {
                return Order == other.Order && string.Equals(RouteTemplate, other.RouteTemplate, StringComparison.OrdinalIgnoreCase);
            }

            public override bool Equals(object obj)
            {
                return obj is Key ? Equals((Key)obj) : false;
            }

            public override int GetHashCode()
            {
                var hash = new HashCodeCombiner();
                hash.Add(Order);
                hash.Add(RouteTemplate, StringComparer.OrdinalIgnoreCase);
                return hash;
            }
        }

        private class Cache
        {
            public readonly UrlMatchingTree[] Trees;

            public Cache(UrlMatchingTree[] trees)
            {
                Trees = trees;
            }
        }

        private struct Treenumerator : IEnumerator<UrlMatchingNode>
        {
            private readonly Stack<UrlMatchingNode> _stack;
            private readonly PathTokenizer _tokenizer;

            public Treenumerator(UrlMatchingNode root, PathTokenizer tokenizer)
            {
                _stack = new Stack<UrlMatchingNode>();
                _tokenizer = tokenizer;
                Current = null;

                _stack.Push(root);
            }

            public UrlMatchingNode Current { get; private set; }

            object IEnumerator.Current => Current;

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                if (_stack == null)
                {
                    return false;
                }

                while (_stack.Count > 0)
                {
                    var next = _stack.Pop();

                    // In case of wild card segment, the request path segment length can be greater
                    // Example:
                    // Template:    a/{*path}
                    // Request Url: a/b/c/d
                    if (next.IsCatchAll && next.Matches.Count > 0)
                    {
                        Current = next;
                        return true;
                    }
                    // Next template has the same length as the url we are trying to match
                    // The only possible matching segments are either our current matches or
                    // any catch-all segment after this segment in which the catch all is empty.
                    else if (next.Depth == _tokenizer.Count)
                    {
                        if (next.Matches.Count > 0)
                        {
                            Current = next;
                            return true;
                        }
                        else
                        {
                            // We can stop looking as any other child node from this node will be
                            // either a literal, a constrained parameter or a parameter.
                            // (Catch alls and constrained catch alls will show up as candidate matches).
                            continue;
                        }
                    }

                    if (next.CatchAlls != null)
                    {
                        _stack.Push(next.CatchAlls);
                    }

                    if (next.ConstrainedCatchAlls != null)
                    {
                        _stack.Push(next.ConstrainedCatchAlls);
                    }

                    if (next.Parameters != null)
                    {
                        _stack.Push(next.Parameters);
                    }

                    if (next.ConstrainedParameters != null)
                    {
                        _stack.Push(next.ConstrainedParameters);
                    }

                    if (next.Literals.Count > 0)
                    {
                        UrlMatchingNode node;
                        Debug.Assert(next.Depth < _tokenizer.Count);
                        if (next.Literals.TryGetValue(_tokenizer[next.Depth].Value, out node))
                        {
                            _stack.Push(node);
                        }
                    }
                }

                return false;
            }

            public void Reset()
            {
                _stack.Clear();
                Current = null;
            }
        }
    }
}
