﻿﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.AspNet.Routing.Legacy
{
    /// <summary>
    /// Represents a URI generated from a <see cref="HttpParsedRoute"/>. 
    /// </summary>
    internal class BoundRouteTemplate
    {
        public string BoundTemplate { get; set; }

        public IDictionary<string, object> Values { get; set; }
    }
}