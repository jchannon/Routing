// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Dispatcher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.AspNetCore.Dispatcher.FunctionalTest
{
    public class ApiAppStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddLogging();
            services.AddDispatcher();
            services.AddRouting();

            services.Configure<DispatcherOptions>(ConfigureDispatcher);
        }

        public void Configure(IApplicationBuilder app, ILogger<ApiAppStartup> logger)
        {
            app.UseDispatcher();

            app.Use(next => async (context) =>
            {
                logger.LogInformation("Executing fake CORS middleware");

                var feature = context.Features.Get<IDispatcherFeature>();
                var policy = feature.Endpoint?.Metadata.OfType<CorsPolicyMetadata>().LastOrDefault();
                logger.LogInformation("using CORS policy {PolicyName}", policy?.Name ?? "default");

                await next(context);
            });

            app.Use(next => async (context) =>
            {
                logger.LogInformation("Executing fake AuthZ middleware");

                var feature = context.Features.Get<IDispatcherFeature>();
                var policy = feature.Endpoint?.Metadata.OfType<AuthorizationPolicyMetadata>().LastOrDefault();
                if (policy != null)
                {
                    logger.LogInformation("using Auth policy {PolicyName}", policy.Name);
                }

                await next(context);
            });
        }

        public void ConfigureDispatcher(DispatcherOptions options)
        {
            options.Dispatchers.Add(new TreeDispatcher()
            {
                Endpoints =
                {
                    new SimpleEndpoint(Products_Get, new object[]{ new RouteTemplateMetadata("api/products"), }),
                },
            });

            options.HandlerFactories.Add(endpoint => (endpoint as SimpleEndpoint)?.HandlerFactory);
        }

        private Task Products_Get(HttpContext httpContext) => httpContext.Response.WriteAsync("Hello, Products_Get");

        private class CorsPolicyMetadata
        {
            public string Name { get; set; }
        }

        private class AuthorizationPolicyMetadata
        {
            public string Name { get; set; }
        }
    }
}
