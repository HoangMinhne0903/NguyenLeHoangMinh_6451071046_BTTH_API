using System;
using System.Collections.Generic;
using System.Linq;

namespace retry_service.Services
{
    public class RetryMetricsService
    {
        private int _totalRetries = 0;
        private int _totalDlq = 0;
        private readonly List<string> _retriedCommandIds = new();
        private readonly List<string> _dlqCommandIds = new();
        private readonly object _lock = new();

        public int TotalRetries => _totalRetries;
        public int TotalDlq => _totalDlq;

        public void IncrementRetries(string commandId)
        {
            lock (_lock)
            {
                _totalRetries++;
                if (!_retriedCommandIds.Contains(commandId))
                {
                    _retriedCommandIds.Add(commandId);
                }
            }
        }

        public void IncrementDlq(string commandId)
        {
            lock (_lock)
            {
                _totalDlq++;
                if (!_dlqCommandIds.Contains(commandId))
                {
                    _dlqCommandIds.Add(commandId);
                }
            }
        }

        public object GetMetrics()
        {
            lock (_lock)
            {
                return new
                {
                    status = "active",
                    totalRetries = _totalRetries,
                    totalDlq = _totalDlq,
                    retriedCommandIds = _retriedCommandIds.ToList(),
                    dlqCommandIds = _dlqCommandIds.ToList(),
                    lastUpdated = DateTime.UtcNow
                };
            }
        }
    }
}
