using System;

namespace SyncClipboardWin
{
    public sealed class TransferProgress
    {
        public string Operation { get; private set; }
        public string ItemName { get; private set; }
        public long TransferredBytes { get; private set; }
        public long TotalBytes { get; private set; }

        public TransferProgress(string operation, string itemName, long transferredBytes, long totalBytes)
        {
            Operation = operation ?? "";
            ItemName = itemName ?? "";
            TransferredBytes = transferredBytes;
            TotalBytes = totalBytes;
        }

        public int Percentage
        {
            get
            {
                if (TotalBytes <= 0) return -1;
                long value = TransferredBytes * 100L / TotalBytes;
                if (value < 0) value = 0;
                if (value > 100) value = 100;
                return (int)value;
            }
        }
    }
}
