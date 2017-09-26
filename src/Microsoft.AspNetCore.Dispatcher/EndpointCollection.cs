// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Dispatcher;

namespace Microsoft.AspNetCore.Dispatcher
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

        public IList<Endpoint> Items { get; set; }
        public int Version { get; set; }
    }
}