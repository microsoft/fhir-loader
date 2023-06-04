// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace FhirLoader.CommandLineTool.CLI
{
    internal class Metrics : IDisposable
    {
        private readonly Meter _meter;
        private readonly Counter<int>? _resourcesProcessed;

        private readonly MeterListener _meterListener = new();
        private readonly ILogger _logger = ApplicationLogging.Instance.CreateLogger("Metrics");

        // Used to print resource rates
        private const int OutputRefresh = 5000;
        private DateTime _lastPrintTime = DateTime.Now;
        private DateTime _startTime = DateTime.Now;
        private DateTime? _stopTime;
        private readonly ConcurrentBag<int> _resourceCount;
        private int _windowIndex;

        public Metrics()
        {
            _meter = new Meter("Applied.FhirLoader.CommandLineTool", "1.0.0");
            _resourcesProcessed = _meter.CreateCounter<int>(name: "resources-processed", unit: "Resources", description: "The number of FHIR resources processed by the server");
            _resourceCount = new ConcurrentBag<int>();

            _meterListener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == _meter.Name)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _meterListener.SetMeasurementEventCallback<int>(OnMeasurementRecorded);
        }

        public static Metrics Instance { get; } = new Metrics();

        // Aggregators
        public long TotalResourcesSent => _resourceCount.Sum();

        public double TotalTimeInMilliseconds => ((_stopTime ?? DateTime.Now) - _startTime).TotalMilliseconds;

        public void Start()
        {
            _meterListener.Start();
            _lastPrintTime = DateTime.Now;
            _startTime = DateTime.Now;
        }

        public void Stop()
        {
            _stopTime = DateTime.Now;
            _meterListener.Dispose();
        }

        // Don't remove the unused time parameter - it's needed as this function is a parameter for sending metrics.
        public void RecordBundlesSent(int resourceCount, long time)
        {
            _resourcesProcessed!.Add(resourceCount);
            _meterListener.RecordObservableInstruments();
        }

        private void OnMeasurementRecorded(Instrument instrument, int measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            if (instrument.Name != _resourcesProcessed!.Name)
            {
                _logger.LogTrace($"{instrument.Name} recorded measurement {measurement}");
                return;
            }

            _resourceCount.Add(measurement);
            _logger.LogDebug($"Bundle processed with {measurement} resources. {TotalResourcesSent} total resources processed.");

            var timeChange = (DateTime.Now - _lastPrintTime).TotalMilliseconds;
            if (timeChange > OutputRefresh)
            {
                _lastPrintTime = DateTime.Now;
                int windowTotal = _resourceCount.Skip(_windowIndex).Sum();
                _windowIndex = _resourceCount.Count;

                int currentRate = (int)(Convert.ToDouble(windowTotal) / (timeChange / 1000));
                int totalRate = (int)(Convert.ToDouble(TotalResourcesSent) / (TotalTimeInMilliseconds / 1000));
                _logger.LogInformation($"Current processing Rate: {currentRate} resources/second. Total processing rate {totalRate} resources/second.");
            }
        }

        public void Dispose()
        {
            _meter.Dispose();
            _meterListener.Dispose();
        }
    }
}
