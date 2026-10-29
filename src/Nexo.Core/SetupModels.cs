using System;

namespace Nexo.Core
{
    public sealed class SetupProgress
    {
        public string Stage { get; }
        public long BytesCompleted { get; }
        public long? TotalBytes { get; }
        public SetupProgress(string stage, long bytesCompleted, long? totalBytes = null)
        { Stage = stage; BytesCompleted = bytesCompleted; TotalBytes = totalBytes; }
    }
    public sealed class EmulatorInstallResult
    {
        public bool Changed { get; }
        public string Message { get; }
        public SystemStatus Status { get; }
        internal EmulatorInstallResult(bool changed, string message, SystemStatus status)
        { Changed = changed; Message = message; Status = status; }
    }
    public sealed class SystemFilesResult
    {
        public bool Changed { get; }
        public string Message { get; }
        public SystemStatus Status { get; }
        internal SystemFilesResult(bool changed, string message, SystemStatus status)
        { Changed = changed; Message = message; Status = status; }
    }
    internal sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> report;
        internal InlineProgress(Action<T> report) { this.report = report; }
        public void Report(T value) => report(value);
    }
}
