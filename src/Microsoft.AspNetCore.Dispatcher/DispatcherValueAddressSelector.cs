﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.AspNetCore.Dispatcher
{
    // This isn't a proposed design, just a placeholder to demonstrate that things are wired up correctly.
    public class DispatcherValueAddressSelector
    {
        private readonly AddressTable _addressTable;

        public DispatcherValueAddressSelector(AddressTable addressTable)
        {
            if (addressTable == null)
            {
                throw new ArgumentNullException(nameof(addressTable));
            }

            _addressTable = addressTable;
        }

        public Address SelectAddress(DispatcherValueCollection values)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            // Capture the current state so we don't see partial updates.
            var groups = _addressTable.AddressGroups;
            for (var i = 0; i < groups.Count; i++)
            {
                var matches = new List<Address>();
                var group = groups[i];

                for (var j = 0; j < group.Count; j++)
                {
                    var address = group[j] as DispatcherValueAddress;
                    if (address == null)
                    {
                        continue;
                    }

                    if (IsMatch(address, values))
                    {
                        matches.Add(address);
                    }
                }

                switch (matches.Count)
                {
                    case 0:
                        // No match, keep going.
                        break;

                    case 1:
                        return matches[0];

                    default:
                        throw new InvalidOperationException("Ambiguous bro!");

                }
            }

            return null;
        }

        private bool IsMatch(DispatcherValueAddress address, DispatcherValueCollection values)
        {
            foreach (var kvp in address.Values)
            {
                values.TryGetValue(kvp.Key, out var value);

                if (!string.Equals(Convert.ToString(kvp.Value) ?? string.Empty, Convert.ToString(value) ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
