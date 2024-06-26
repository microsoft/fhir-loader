using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.EventGrid;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Collections.Generic;
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
        [FunctionName("ImportBundleQueue")]
        public async Task Run([QueueTrigger("bundlequeue", Connection = "FBI-STORAGEACCT-QUEUEURI-IDENTITY")] JObject blobCreatedEvent, ILogger log)
        {
            string url = (string)blobCreatedEvent["data"]["url"];
            log.LogInformation($"ImportBundleEventGrid: Processing blob at {url}...");
            string container = Utils.GetEnvironmentVariable("FBI-CONTAINER-BUNDLES", "bundles");
            string name = url.Substring(url.IndexOf($"/{container}/") + $"/{container}/".Length);
            await ImportUtils.ImportBundle(name, log, _telemetryClient);
        }
        [FunctionName("PoisonQueueRetries")]
       public static async Task PoisonQueueRetries(
       [TimerTrigger("%FBI-POISONQUEUE-TIMER-CRON%")] TimerInfo timerInfo,
       ILogger log)
        {
            log.LogInformation($"PoisonQueueRetries:Checking for poison queue messages in bundlequeue-poison...");
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
                log.LogInformation($"PoisonQueueRetries:Found {cachedMessagesCount} messages in bundlequeue-poison....Re-queing upto {maxrequeuemessages}");
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
                log.LogInformation($"PoisonQueueRetries:Requeued {messagesrequeued} messages to bundlequeue");
            }

        }
    }
}
