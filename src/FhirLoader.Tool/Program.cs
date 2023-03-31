// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks.Dataflow;
using Azure.Identity;
using CommandLine;
using FhirLoader.Tool.CLI;
using FhirLoader.Tool.Client;
using FhirLoader.Tool.FileSource;
using FhirLoader.Tool.FileType;
using FhirLoader.Tool.FileTypeHandlers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FhirLoader.Tool
{
    public class Program
    {
        private static ILogger<Program> _logger = ApplicationLogging.Instance.CreateLogger<Program>();
        private static CancellationTokenSource _cancelTokenSource = new CancellationTokenSource();

        internal static async Task<int> Main(string[] args)
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
            _logger.LogInformation("Setting up Microsoft FHIR Loader Tool, please wait...  ");
            try
            {
                // Create client and setup access token
                var client = new FhirResourceClient(opt.FhirUrl!, opt.ConcurrencyInternal, opt.SkipErrors, _logger, opt.TenantId, opt.Audience, _cancelTokenSource.Token);

                // Get file streams from the specified source
                BaseFileSource fileSource = ParseFileSource(opt);

                // Start our metrics tracking for the console output.
                Metrics.Instance.Start();

                // Send bundles in parallel
                try
                {
                    var actionBlock = new ActionBlock<BaseProcessedResource>(
                        async bundleWrapper =>
                    {
                        await client.Send(bundleWrapper, Metrics.Instance.RecordBundlesSent, _cancelTokenSource.Token);
                    },
                        new ExecutionDataflowBlockOptions
                       {
                           MaxDegreeOfParallelism = opt.Concurrency!.Value,
                           BoundedCapacity = opt.Concurrency!.Value * 2,
                           CancellationToken = _cancelTokenSource.Token,
                       });

                    // For each file, send segmented bundles
                    bool blockAcceptingNewMessages = true;

                    foreach (var file in fileSource.Files)
                    {
                        if (!blockAcceptingNewMessages)
                        {
                            break;
                        }

                        var processedResourceList = BaseProcessedResource.ProcessedResourceFromFileStream(file.Data, file.Name, opt.BatchSizeInternal, _logger);

                        foreach (var resource in processedResourceList)
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
                        var message = string.IsNullOrEmpty(ex.Message) ? ex.InnerException?.Message : ex.Message;
                        _logger.LogCritical("Failed to send all bundles because: ", ex.Message);
                    }
                }
                catch (AggregateException ae)
                {
                    // If an unhandled exception occurs during dataflow processing, all
                    // exceptions are propagated through an AggregateException object.
                    ae.Handle(e =>
                    {
                        Console.WriteLine("Encountered {0}: {1}", e.GetType().Name, e.Message);
                        return true;
                    });
                }

                Console.WriteLine($"Done! Sent {Metrics.Instance.TotalResourcesSent} resources in {(int)(Metrics.Instance.TotalTimeInMilliseconds / 1000)} seconds.");
                if (opt.PackagePath is not null)
                {
                    await client.ReIndex(opt.FhirUrl!);
                }
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

            if (ApplicationLogging.Instance.LogFactory is not null)
            {
                ApplicationLogging.Instance.LogFactory.Dispose();
            }

            Metrics.Instance.Stop();
            return 0;
        }

        internal static BaseFileSource ParseFileSource(CommandOptions opt)
        {
            if (opt.FolderPath is not null)
            {
                return new LocalFolderSource(opt.FolderPath, _logger);
            }
            else if (opt.BlobPath is not null)
            {
                return new AzureBlobSource(opt.BlobPath, _logger);
            }
            else if (opt.PackagePath is not null)
            {
                // # TODO - fix metadata parameter issues.
                // JObject? metadata = await client.Get("/metadata");
                return new FhirPackageSource(opt.PackagePath, _logger);
            }

            throw new ArgumentException("Unknown input type.");
        }

        internal static void OnConsoleCancel(object? sender, ConsoleCancelEventArgs e)
        {
            _logger.LogWarning("Gracefully shutting down...");
            _cancelTokenSource.Cancel();
            e.Cancel = true;
        }
    }
}
