using Azure.Identity;
using CommandLine;
using FhirLoader.Common;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks.Dataflow;


namespace FhirLoader.Tool
{
    public class Program
    {
        private static ILogger<Program> _logger = ApplicationLogging.Instance.CreateLogger<Program>();
        private static CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();

        static async Task<int> Main(string[] args)
        {
            Console.CancelKeyPress += OnConsoleCancel;

            try
            {
                return await Parser.Default.ParseArguments<CommandOptions>(args)
                .MapResult(
                    async (CommandOptions opt) =>
                    {
                        if (opt.Debug)
                        {
                            _logger = ApplicationLogging.Instance.Configure(LogLevel.Trace).CreateLogger<Program>();
                        }
                        else
                        {
                            _logger = ApplicationLogging.Instance.Configure(LogLevel.Information).CreateLogger<Program>();
                        }

                        opt.Validate();
                        return await Run(opt);
                    },
                    _ => Task.FromResult(1));
            }
            catch (ArgumentValidationException ex)
            {
                Console.Error.WriteLine("Invalid arguement detected: {0}", ex);
                return 1;
            }
        }

        public static async Task<int> Run(CommandOptions opt)
        {
            _logger.LogInformation("Setting up Applied FHIR Loader, please wait...  ");
            try
            {
                IEnumerable<FhirFileHandler> files;
                SourceFileHandler sourceHandler = new(_logger);

                if (opt.FolderPath is not null)
                    files = sourceHandler.LoadFromFilePath(opt.FolderPath, opt.BatchSize!.Value);
                else if (opt.BlobPath is not null)
                    files = sourceHandler.LoadFromBlobPath(opt.BlobPath, opt.BatchSize!.Value);
                else if (opt.PackagePath is not null)
                    files = sourceHandler.LoadFromPackagePath(opt.PackagePath, opt.BatchSize!.Value, opt.BundlePackageFiles);
                else
                    throw new ArgumentException("Either folder,package or blob must be inputted.");

                // Create client and setup access token
                var client = new FhirResourceClient(opt.FhirUrl!, opt.Concurrency ?? 10, opt.SkipErrors,  _logger, opt.TenantId);
                await client.PrefetchToken(_cancelTokenSource.Token);

                // Create a bundle sender
                Metrics.Instance.Start();

                // Send bundles in parallel
                try
                {
                    var actionBlock = new ActionBlock<ProcessedResource>(async bundleWrapper =>
                    {
                        await client.Send(bundleWrapper, Metrics.Instance.RecordBundlesSent, _cancelTokenSource.Token);
                    },
                       new ExecutionDataflowBlockOptions
                       {
                           MaxDegreeOfParallelism = opt.Concurrency!.Value,
                           BoundedCapacity = opt.Concurrency!.Value * 2,
                           CancellationToken = _cancelTokenSource.Token
                       }
                   );

                    // For each file, send segmented bundles
                    bool blockAcceptingNewMessages = true;
                    foreach (var file in files)
                    {
                        if (!blockAcceptingNewMessages)
                            break;

                        foreach (var resource in file.FileAsResourceList!)
                        {
                            if (opt.StripText)
                            {
                                var resourceObj = JObject.Parse(resource.ResourceText!);
                                resourceObj["text"] = new JObject();
                                resource.ResourceText = resourceObj.ToString();
                            }

                            if (!await actionBlock.SendAsync(resource, _cancelTokenSource.Token))
                            {
                                blockAcceptingNewMessages = false;
                                _logger.LogError("Cannot send all bundles due to an internal error. Finishing and exiting.");
                                break;
                            }
                        }
                    }

                    actionBlock.Complete();
                    await actionBlock.Completion.WaitAsync(_cancelTokenSource.Token);
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException)
                {
                    _logger.LogWarning("Exiting...");
                }
                catch (FatalFhirResourceClientException ex)
                {
                    if (ex.InnerException is not TaskCanceledException)
                    {
                        _cancelTokenSource.Cancel();
                        var message = String.IsNullOrEmpty(ex.Message) ? ex.InnerException?.Message : ex.Message;
                        _logger.LogCritical("Failed to send all bundles because: ", ex.Message);
                    }
                }
                catch (AggregateException ae)
                {
                    // If an unhandled exception occurs during dataflow processing, all
                    // exceptions are propagated through an AggregateException object.
                    ae.Handle(e =>
                    {
                        Console.WriteLine("Encountered {0}: {1}",
                           e.GetType().Name, e.Message);
                        return true;
                    });
                }
                Console.WriteLine($"Done! Sent {Metrics.Instance.TotalResourcesSent} resources in {(int)(Metrics.Instance.TotalTimeInMilliseconds / 1000)} seconds.");
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex.Message);
                Console.WriteLine(ex.Message);
            }
            catch (DirectoryNotFoundException)
            {
                _logger.LogError($"Could not find path {opt.FolderPath}");
            }
            catch (CredentialUnavailableException)
            {
                _logger.LogError($"Could not obtain Azure credential. Please use `az login` or another method specified here: https://docs.microsoft.com/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet");
            }

            ApplicationLogging.Instance.LogFactory.Dispose();
            Metrics.Instance.Stop();
            return 0;
        }

        static void OnConsoleCancel(object? sender, ConsoleCancelEventArgs e)
        {
            _logger.LogWarning("Gracefully shutting down...");
            _cancelTokenSource.Cancel();
            e.Cancel = true;
        }
    }
}