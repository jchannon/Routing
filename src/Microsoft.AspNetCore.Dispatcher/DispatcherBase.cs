// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Dispatcher
{
    public abstract class DispatcherBase : IAddressCollectionProvider, IEndpointCollectionProvider
    {
        private List<Address> _addresses;
        private IEndpointCollection _endpoints;
        private List<EndpointSelector> _endpointSelectors;

        public virtual IList<Address> Addresses
        {
            get
            {
                if (_addresses == null)
                {
                    _addresses = new List<Address>();
                }

                return _addresses;
            }
        }

        public virtual IEndpointCollection Endpoints
        {
            get
            {
                if (_endpoints == null)
                {
                    _endpoints = new EndpointCollection(null, 0);
                }

                return _endpoints;
            }

            set
            {
                _endpoints = value;
            }
        }

        public virtual IList<EndpointSelector> Selectors
        {
            get
            {
                if (_endpointSelectors == null)
                {
                    _endpointSelectors = new List<EndpointSelector>();
                }

                return _endpointSelectors;
            }
        }

        public IChangeToken ChangeToken => NullChangeToken.Singleton;

        IReadOnlyList<Address> IAddressCollectionProvider.Addresses => _addresses;

        IEndpointCollection IEndpointCollectionProvider.Endpoints => _endpoints;

        public virtual async Task InvokeAsync(HttpContext httpContext)
        {
            if (httpContext == null)
            {
                throw new ArgumentNullException(nameof(httpContext));
            }

            var feature = httpContext.Features.Get<IDispatcherFeature>();
            if (await TryMatchAsync(httpContext))
            {
                if (feature.RequestDelegate != null)
                {
                    // Short circuit, no need to select an endpoint.
                    return;
                }

                var selectorContext = new EndpointSelectorContext(httpContext, Endpoints, Selectors);
                await selectorContext.InvokeNextAsync();

                switch (selectorContext.Endpoints.Items.Count)
                {
                    case 0:
                        break;

                    case 1:
                        
                        feature.Endpoint = selectorContext.Endpoints.Items[0];
                        break;

                    default:
                        throw new InvalidOperationException("Ambiguous bro!");

                }
            }
        }

        protected abstract Task<bool> TryMatchAsync(HttpContext httpContext);
    }
}
