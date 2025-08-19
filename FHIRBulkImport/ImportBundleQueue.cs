using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Collections.Generic;
using Microsoft.Azure.Functions.Worker.Extensions.Timer;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Azure.Identity;

namespace FHIRBulkImport
{
    
    public class ImportBundleQueue
    {
        private readonly TelemetryClient _telemetryClient;
        public ImportBundleQueue(TelemetryConfiguration telemetryConfiguration)
        {
            _telemetryClient = new TelemetryClient(telemetryConfiguration);
            
        }
        [Function("ImportBundleQueue")]
        public async Task Run([QueueTrigger("bundlequeue", Connection = "FBI-STORAGEACCT-QUEUEURI-IDENTITY")] JObject blobCreatedEvent, FunctionContext context)
        {
            var logger = context.GetLogger("ImportBundleQueue");
            string url = (string)blobCreatedEvent["data"]["url"];
            logger.LogInformation($"ImportBundleEventGrid: Processing blob at {url}...");
            string container = Utils.GetEnvironmentVariable("FBI-CONTAINER-BUNDLES", "bundles");
            string name = url.Substring(url.IndexOf($"/{container}/") + $"/{container}/".Length);
            await ImportUtils.ImportBundle(name, logger, _telemetryClient);
        }
        [Function("PoisonQueueRetries")]
       public static async Task PoisonQueueRetries(
       [TimerTrigger("%FBI-POISONQUEUE-TIMER-CRON%")] TimerInfo timerInfo,
       FunctionContext context)
        {
            var logger = context.GetLogger("PoisonQueueRetries");
            logger.LogInformation($"PoisonQueueRetries:Checking for poison queue messages in bundlequeue-poison...");
            var sourceQueue = new QueueClient(new Uri($"{Utils.GetEnvironmentVariable("FBI-STORAGEACCT-QUEUEURI")}/bundlequeue-poison"),new DefaultAzureCredential());
            await sourceQueue.CreateIfNotExistsAsync();
            var targetQueue = new QueueClient(new Uri($"{Utils.GetEnvironmentVariable("FBI-STORAGEACCT-QUEUEURI")}/bundlequeue"), new DefaultAzureCredential());
            await targetQueue.CreateIfNotExistsAsync();
            int maxrequeuemessages = Utils.GetIntEnvironmentVariable("FBI-MAXREQUEUE-MESSAGE-COUNT", "100");
            int messagesrequeued = 0;
            if (await sourceQueue.ExistsAsync())
            {
                QueueProperties properties = sourceQueue.GetProperties();
                // Retrieve the cached approximate message count.
                int cachedMessagesCount = properties.ApproximateMessagesCount;
                logger.LogInformation($"PoisonQueueRetries:Found {cachedMessagesCount} messages in bundlequeue-poison....Re-queing upto {maxrequeuemessages}");
                while(cachedMessagesCount > 0 && messagesrequeued < maxrequeuemessages) {
                    int batchsize = (maxrequeuemessages - messagesrequeued >= 32 ? 32 : maxrequeuemessages - messagesrequeued);
                    foreach (var message in sourceQueue.ReceiveMessages(maxMessages: batchsize).Value)
                    {
                        var res = await targetQueue.SendMessageAsync(message.Body);
                        await sourceQueue.DeleteMessageAsync(message.MessageId, message.PopReceipt);
                        messagesrequeued++;
                    }
                    properties = sourceQueue.GetProperties();
                    cachedMessagesCount = properties.ApproximateMessagesCount;
                }
                logger.LogInformation($"PoisonQueueRetries:Requeued {messagesrequeued} messages to bundlequeue");
            }

        }
    }
}
