// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace FhirLoader.Tool.CLI
{
    internal class Metrics
    {
        public static Metrics Instance = new Metrics();

        private Meter s_meter;
        static Counter<int>? s_resourcesProcessed;

        private MeterListener meterListener = new MeterListener();
        private ILogger _logger = ApplicationLogging.Instance.CreateLogger("Metrics");

        // Used to print resource rates
        const int OUTPUT_REFRESH = 5000;
        private DateTime _lastPrintTime = DateTime.Now;
        private DateTime _startTime = DateTime.Now;
        private DateTime? _stopTime;
        private readonly ConcurrentBag<int> _resourceCount;
        private int _windowIndex = 0;

        // Aggregators
        public long TotalResourcesSent => _resourceCount.Sum();
        public double TotalTimeInMilliseconds => ((_stopTime ?? DateTime.Now) - _startTime).TotalMilliseconds;

        public Metrics()
        {
            s_meter = new Meter("Applied.FhirLoader.Tool", "1.0.0");
            s_resourcesProcessed = s_meter.CreateCounter<int>(name: "resources-processed", unit: "Resources", description: "The number of FHIR resources processed by the server");
            _resourceCount = new ConcurrentBag<int>();

            meterListener = new MeterListener();
            meterListener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == s_meter.Name)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            meterListener.SetMeasurementEventCallback<int>(OnMeasurementRecorded);
        }


        public void Start()
        {
            meterListener.Start();
            _lastPrintTime = DateTime.Now;
            _startTime = DateTime.Now;
        }

        public void Stop()
        {
            _stopTime = DateTime.Now;
            meterListener.Dispose();
        }

        public void RecordBundlesSent(int resourceCount, long time)
        {
            s_resourcesProcessed!.Add(resourceCount);
            meterListener.RecordObservableInstruments();
        }

        void OnMeasurementRecorded(Instrument instrument, int measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        {
            if (instrument.Name != s_resourcesProcessed!.Name)
            {
                _logger.LogTrace($"{instrument.Name} recorded measurement {measurement}");
                return;
            }

            _resourceCount.Add(measurement);
            _logger.LogDebug($"Bundle processed with {measurement} resources. {TotalResourcesSent} total resources processed.");

            var timeChange = (DateTime.Now - _lastPrintTime).TotalMilliseconds;
            if (timeChange > OUTPUT_REFRESH)
            {
                _lastPrintTime = DateTime.Now;
                int windowTotal = _resourceCount.Skip(_windowIndex).Sum();
                _windowIndex = _resourceCount.Count;

                int currentRate = (int)(Convert.ToDouble(windowTotal) / (timeChange / 1000));
                int totalRate = (int)(Convert.ToDouble(TotalResourcesSent) / (TotalTimeInMilliseconds / 1000));
                _logger.LogInformation($"Current processing Rate: {currentRate} resources/second. Total processing rate {totalRate} resources/second.");
            }
        }
    }
}
