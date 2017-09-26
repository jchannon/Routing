// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Dispatcher;

namespace DispatcherSample
{
    public class EndpointCollection : IEndpointCollection
    {
        public EndpointCollection(List<Endpoint> endpoints, int version)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }

            Items = endpoints;
            Version = version;
        }

        public IList<Endpoint> Items { get; }

        public int Version { get; }
    }
}