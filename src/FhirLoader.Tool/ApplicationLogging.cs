using Microsoft.Extensions.Logging;
using System.Net.Sockets;

namespace FhirLoader.Tool
{
    /// <summary>
    /// Class to create loggers for our application.
    /// https://stackoverflow.com/a/65046691
    /// </summary>
    public class ApplicationLogging
    {
        public static ApplicationLogging Instance = new ApplicationLogging();
        public ILoggerFactory? LogFactory = null;

        public ApplicationLogging()
        {
            LogFactory = GetLogFactory()!;
        }

        public ApplicationLogging Configure(LogLevel level)
        {
            LogFactory = GetLogFactory(level);
            return this;
        }

        internal ILoggerFactory GetLogFactory(LogLevel level = LogLevel.Information )
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

        public ILogger<T> CreateLogger<T>(){
            if (LogFactory is null)
            {
                LogFactory = GetLogFactory();
            }

            return LogFactory.CreateLogger<T>();

        } 

        public ILogger CreateLogger(string name) {
            if (LogFactory is null)
            {
                LogFactory = GetLogFactory();
            }

            return LogFactory.CreateLogger(name);
        }
    }
}
