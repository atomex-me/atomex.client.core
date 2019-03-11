using System;

namespace Atomix.Core
{
    public class ExecutionReportEventArgs : EventArgs
    {
        public ExecutionReport Report { get; }

        public ExecutionReportEventArgs(ExecutionReport report)
        {
            Report = report;
        }
    }
}