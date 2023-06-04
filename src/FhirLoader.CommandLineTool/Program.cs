// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks.Dataflow;
using Azure.Identity;
using CommandLine;
using FhirLoader.CommandLineTool.CLI;
using FhirLoader.CommandLineTool.Client;
using FhirLoader.CommandLineTool.FileSource;
using FhirLoader.CommandLineTool.FileType;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace FhirLoader.CommandLineTool
{
    public class Program
    {
        private static ILogger<Program> s_logger = ApplicationLogging.Instance.CreateLogger<Program>();
        private static readonly CancellationTokenSource s_cancelTokenSource = new();

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
                            s_logger = ApplicationLogging.Instance.Configure(LogLevel.Trace).CreateLogger<Program>();
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
            if (opt is null)
            {
                throw new ArgumentNullException(nameof(opt));
            }

            s_logger.LogInformation("Setting up Microsoft FHIR Loader Tool, please wait...  ");

            try
            {
                // Create client and setup access token
                var client = new FhirResourceClient(opt.FhirUriInternal, opt.ConcurrencyInternal, opt.SkipErrors, s_logger, opt.TenantId, opt.Audience, s_cancelTokenSource.Token);

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
                            await client.Send(bundleWrapper, Metrics.Instance.RecordBundlesSent, s_cancelTokenSource.Token);
                        },
                        new ExecutionDataflowBlockOptions
                        {
                            MaxDegreeOfParallelism = opt.Concurrency!.Value,
                            BoundedCapacity = opt.Concurrency!.Value * 2,
                            CancellationToken = s_cancelTokenSource.Token,
                        });

                    // For each file, send segmented bundles
                    bool blockAcceptingNewMessages = true;

                    foreach ((string fileName, Stream fileStream) in fileSource.Files)
                    {
                        if (!blockAcceptingNewMessages)
                        {
                            break;
                        }

                        IEnumerable<BaseProcessedResource> processedResourceList = BaseProcessedResource.ProcessedResourceFromFileStream(fileStream, fileName, opt.BatchSizeInternal, s_logger);

                        foreach (BaseProcessedResource resource in processedResourceList)
                        {
                            if (opt.StripText)
                            {
                                var resourceObj = JObject.Parse(resource.ResourceText!);
                                resourceObj["text"] = new JObject();
                                resource.ResourceText = resourceObj.ToString();
                            }

                            if (!await actionBlock.SendAsync(resource, s_cancelTokenSource.Token))
                            {
                                blockAcceptingNewMessages = false;
                                s_logger.LogError("Cannot send all bundles due to an internal error. Finishing and exiting.");
                                break;
                            }
                        }
                    }

                    actionBlock.Complete();
                    await actionBlock.Completion.WaitAsync(s_cancelTokenSource.Token);
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex is OperationCanceledException)
                {
                    s_logger.LogWarning("Exiting...");
                }
                catch (FatalFhirResourceClientException ex)
                {
                    if (ex.InnerException is not TaskCanceledException)
                    {
                        s_cancelTokenSource.Cancel();
                        var message = string.IsNullOrEmpty(ex.Message) ? ex.InnerException?.Message : ex.Message;
                        s_logger.LogCritical("Failed to send all bundles because: ", ex.Message);
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
                    await client.ReIndex();
                }
            }
            catch (ArgumentException ex)
            {
                s_logger.LogError(ex.Message);
                Console.WriteLine(ex.Message);
            }
            catch (DirectoryNotFoundException)
            {
                s_logger.LogError($"Could not find path {opt.FolderPath}");
            }
            catch (CredentialUnavailableException)
            {
                s_logger.LogError($"Could not obtain Azure credential. Please use `az login` or another method specified here: https://docs.microsoft.com/dotnet/api/azure.identity.defaultazurecredential?view=azure-dotnet");
            }

            ApplicationLogging.Instance.LogFactory?.Dispose();

            Metrics.Instance.Stop();
            return 0;
        }

        internal static BaseFileSource ParseFileSource(CommandOptions opt)
        {
            return opt.InputType switch
            {
                InputType.LocalFolder => new LocalFolderSource(opt.FolderPath!, s_logger),
                InputType.Blob => new AzureBlobSource(opt.BlobPathInternal, s_logger),
                InputType.LocalPackage => new FhirPackageSource(opt.PackagePath!, s_logger),
                _ => throw new ArgumentException("Unknown input type."),
            };
        }

        internal static void OnConsoleCancel(object? sender, ConsoleCancelEventArgs e)
        {
            s_logger.LogWarning("Gracefully shutting down...");
            s_cancelTokenSource.Cancel();
            e.Cancel = true;
        }
    }
}
