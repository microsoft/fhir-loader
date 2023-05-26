// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;

namespace FhirLoader.CommandLineTool.CLI
{
    /// <summary>
    /// Class to create loggers for our application.
    /// https://stackoverflow.com/a/65046691.
    /// </summary>
    public class ApplicationLogging
    {
        public ApplicationLogging()
        {
            LogFactory = GetLogFactory()!;
        }

        public static ApplicationLogging Instance { get; } = new ApplicationLogging();

        public ILoggerFactory? LogFactory { get; set;  }

        public ApplicationLogging Configure(LogLevel level)
        {
            LogFactory = GetLogFactory(level);
            return this;
        }

        internal static ILoggerFactory GetLogFactory(LogLevel level = LogLevel.Information)
        {
            return LoggerFactory.Create(builder =>
            {
                builder.ClearProviders();

                // Clear Microsoft's default providers (like eventlogs and others)
                builder.AddSimpleConsole(options =>
                {
                    options.IncludeScopes = false;
                    options.TimestampFormat = "HH:mm:ss ";
                    options.SingleLine = true;
                })
                .SetMinimumLevel(level);
            });
        }

        public ILogger<T> CreateLogger<T>()
        {
            if (LogFactory is null)
            {
                LogFactory = GetLogFactory();
            }

            return LogFactory.CreateLogger<T>();
        }

        public ILogger CreateLogger(string name)
        {
            if (LogFactory is null)
            {
                LogFactory = GetLogFactory();
            }

            return LogFactory.CreateLogger(name);
        }
    }
}
