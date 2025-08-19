using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
public class Program
{
    private static async Task Main(string[] args)
    {
        var host = new HostBuilder()
        .ConfigureAppConfiguration(config =>
        {
            config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables();
        })
        .ConfigureFunctionsWorkerDefaults()
        .ConfigureServices(services => {
            services.AddApplicationInsightsTelemetryWorkerService();
            services.ConfigureFunctionsApplicationInsights();
        })
        .ConfigureLogging(logging =>
        {
            logging.AddApplicationInsights();
        })
        .Build();
        host.Run();
        {

        }
        }
    }
