﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Dispatcher;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Dispatcher;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DispatcherSample
{
    public class Startup
    {
        private readonly static IInlineConstraintResolver ConstraintResolver = new DefaultInlineConstraintResolver(
            new OptionsManager<RouteOptions>(
                new OptionsFactory<RouteOptions>(
                    Enumerable.Empty<IConfigureOptions<RouteOptions>>(),
                    Enumerable.Empty<IPostConfigureOptions<RouteOptions>>())));

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<DispatcherOptions>(options =>
            {
                options.Dispatchers.Add(new RouteTemplateDispatcher("{controller=Home}/{action=Index}/{id?}", ConstraintResolver)
                {
                    Addresses =
                    {
                        new DispatcherValueAddress(new { controller = "Home", action = "Index", }, new object[]{ new RouteTemplateMetadata("{controller=Home}/{action=Index}/{id?}"), }, "Home:Index()"),
                        new DispatcherValueAddress(new { controller = "Home", action = "About", }, new object[]{ new RouteTemplateMetadata("{controller=Home}/{action=Index}/{id?}"), }, "Home:About()"),
                        new DispatcherValueAddress(new { controller = "Admin", action = "Index", }, new object[]{ new RouteTemplateMetadata("{controller=Home}/{action=Index}/{id?}"), }, "Admin:Index()"),
                        new DispatcherValueAddress(new { controller = "Admin", action = "Users", }, new object[]{ new RouteTemplateMetadata("{controller=Home}/{action=Index}/{id?}"), }, "Admin:GetUsers()/Admin:EditUsers()"),
                    },
                    Endpoints = new EndpointCollection(new List<Endpoint>
                    {
                        new SimpleEndpoint(Home_Index, Array.Empty<object>(), new { controller = "Home", action = "Index", }, "Home:Index()"),
                        new SimpleEndpoint(Home_About, Array.Empty<object>(), new { controller = "Home", action = "About", }, "Home:About()"),
                        new SimpleEndpoint(Admin_Index, Array.Empty<object>(), new { controller = "Admin", action = "Index", }, "Admin:Index()"),
                        new SimpleEndpoint(Admin_GetUsers, new object[] { new HttpMethodMetadata("GET"), new AuthorizationPolicyMetadata("Admin"), }, new { controller = "Admin", action = "Users", }, "Admin:GetUsers()"),
                        new SimpleEndpoint(Admin_EditUsers, new object[] { new HttpMethodMetadata("POST"), new AuthorizationPolicyMetadata("Admin"), }, new { controller = "Admin", action = "Users", }, "Admin:EditUsers()"),
                    }, 0),
                    Selectors =
                    {
                        new DispatcherValueEndpointSelector(),
                        new HttpMethodEndpointSelector(),
                    }
                });

                options.HandlerFactories.Add((endpoint) => (endpoint as SimpleEndpoint)?.HandlerFactory);
            });

            services.AddDispatcher();
            services.AddRouting();
            services.AddSingleton<RouteTemplateUrlGenerator>();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILogger<Startup> logger)
        {
            app.UseDispatcher();

            app.Use(async (context, next) =>
            {
                logger.LogInformation("Executing fake CORS middleware");

                var feature = context.Features.Get<IDispatcherFeature>();
                var policy = feature.Endpoint?.Metadata.OfType<ICorsPolicyMetadata>().LastOrDefault();
                logger.LogInformation("using CORS policy {PolicyName}", policy?.Name ?? "default");

                await next.Invoke();
            });

            app.Use(async (context, next) =>
            {
                logger.LogInformation("Executing fake AuthZ middleware");

                var feature = context.Features.Get<IDispatcherFeature>();
                var policy = feature.Endpoint?.Metadata.OfType<IAuthorizationPolicyMetadata>().LastOrDefault();
                if (policy != null)
                {
                    logger.LogInformation("using Auth policy {PolicyName}", policy.Name);
                }

                await next.Invoke();
            });
        }

        public static Task Home_Index(HttpContext httpContext)
        {
            var url = httpContext.RequestServices.GetService<RouteTemplateUrlGenerator>();
            return httpContext.Response.WriteAsync(
                $"<html>" +
                $"<body>" +
                $"<h1>Some links you can visit</h1>" +
                $"<p><a href=\"{url.GenerateUrl(httpContext, new { controller = "Home", action = "Index", })}\">Home:Index()</a></p>" +
                $"<p><a href=\"{url.GenerateUrl(httpContext, new { controller = "Home", action = "About", })}\">Home:About()</a></p>" +
                $"<p><a href=\"{url.GenerateUrl(httpContext, new { controller = "Admin", action = "Index", })}\">Admin:Index()</a></p>" +
                $"<p><a href=\"{url.GenerateUrl(httpContext, new { controller = "Admin", action = "Users", })}\">Admin:GetUsers()/Admin:EditUsers()</a></p>" +
                $"</body>" +
                $"</html>");
        }

        public static Task Home_About(HttpContext httpContext)
        {
            return httpContext.Response.WriteAsync(
                $"<html>" +
                $"<body>" +
                $"<p>This is a dispatcher sample.</p>" +
                $"</body>" +
                $"</html>");
        }

        public static Task Admin_Index(HttpContext httpContext)
        {
            return httpContext.Response.WriteAsync(
                $"<html>" +
                $"<body>" +
                $"<p>This is the admin page.</p>" +
                $"</body>" +
                $"</html>");
        }

        public static Task Admin_GetUsers(HttpContext httpContext)
        {
            return httpContext.Response.WriteAsync(
                $"<html>" +
                $"<body>" +
                $"<p>Users: rynowak, jbagga</p>" +
                $"</body>" +
                $"</html>");
        }

        public static Task Admin_EditUsers(HttpContext httpContext)
        {
            return httpContext.Response.WriteAsync(
                $"<html>" +
                $"<body>" +
                $"<p>blerp</p>" +
                $"</body>" +
                $"</html>");
        }
    }
}
