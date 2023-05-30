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
        /// <summary>
        /// Initializes a new instance of the <see cref="ApplicationLogging"/> class. Creates the Log Factory.
        /// </summary>
        public ApplicationLogging()
        {
            LogFactory = GetLogFactory()!;
        }

        /// <summary>
        /// Gets the singleton instance of the <see cref="ApplicationLogging"/> class.
        /// </summary>
        public static ApplicationLogging Instance { get; } = new ApplicationLogging();

        /// <summary>
        /// Gets or sets the Log Factory.
        /// </summary>
        public ILoggerFactory? LogFactory { get; set;  }

        /// <summary>
        /// Configures the Log Factory with the specified log level.
        /// </summary>
        /// <param name="level">LogLevel to output.</param>
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

        /// <summary>
        /// Creates a logger for the specified type.
        /// </summary>
        /// <typeparam name="T">Object type for the logger.</typeparam>
        public ILogger<T> CreateLogger<T>()
        {
            LogFactory ??= GetLogFactory();

            return LogFactory.CreateLogger<T>();
        }

        /// <summary>
        /// Creates a named logger.
        /// </summary>
        /// <param name="name">Name for the logger.</param>
        public ILogger CreateLogger(string name)
        {
            LogFactory ??= GetLogFactory();

            return LogFactory.CreateLogger(name);
        }
    }
}
