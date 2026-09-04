namespace SyncClipboardWin
{
    public sealed class SyncClipboardModel
    {
        public string Type { get; set; }
        public string Hash { get; set; }
        public string Text { get; set; }
        public bool HasData { get; set; }
        public string DataName { get; set; }
        public long Size { get; set; }

        public SyncClipboardModel()
        {
            Type = "Text";
            Hash = "";
            Text = "";
            DataName = null;
        }
    }
}
