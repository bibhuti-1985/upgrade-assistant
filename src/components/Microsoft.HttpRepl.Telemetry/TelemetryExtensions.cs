﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.DotNet.UpgradeAssistant.Telemetry
{
    public static class TelemetryExtensions
    {
        public static void AddTelemetry(this IServiceCollection services, Action<TelemetryOptions> configure)
        {
            services.AddOptions<TelemetryOptions>()
                .Configure(configure);

            services.AddSingleton<IFirstTimeUseNoticeSentinel, FirstTimeUseNoticeSentinel>();
        }
    }
}
