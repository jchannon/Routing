﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Dispatcher;

namespace DispatcherSample
{
    public class HttpMethodEndpointSelector : EndpointSelector
    {
        public override async Task SelectAsync(EndpointSelectorContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var snapshot = context.CreateSnapshot();

            var fallback = new List<Endpoint>();
            for (var i = context.Endpoints.Items.Count - 1; i >= 0; i--)
            {
                var endpoint = context.Endpoints.Items[i];
                IHttpMethodMetadata metadata = null;

                for (var j = endpoint.Metadata.Count - 1; j >= 0; j--)
                {
                    metadata = endpoint.Metadata[j] as IHttpMethodMetadata;
                    if (metadata != null)
                    {
                        break;
                    }
                }

                if (metadata == null)
                {
                    // No metadata.
                    fallback.Add(endpoint);
                    context.Endpoints.Items.RemoveAt(i);
                }
                else if (Matches(metadata, context.HttpContext.Request.Method))
                {
                    // Do thing, this one matches
                }
                else
                {
                    // Not a match.
                    context.Endpoints.Items.RemoveAt(i);
                }
            }

            // Now the list of endpoints only contains those that have an HTTP method preference AND match the current
            // request.
            await context.InvokeNextAsync();

            if (context.Endpoints.Items.Count == 0)
            {
                // Nothing matched, do the fallback.
                context.RestoreSnapshot(snapshot);
                context.Endpoints.Items.Clear();

                for (var i = 0; i < fallback.Count; i++)
                {
                    context.Endpoints.Items.Add(fallback[i]);
                }

                await context.InvokeNextAsync();
            }
        }

        private bool Matches(IHttpMethodMetadata metadata, string httpMethod)
        {
            for (var i = 0; i < metadata.AllowedMethods.Count; i++)
            {
                if (string.Equals(metadata.AllowedMethods[i], httpMethod, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
