using System;
using System.Drawing;
using System.Windows.Forms;

namespace SyncClipboardWin
{
    public sealed class ProgressForm : Form
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly Label _titleLabel;
        private readonly Label _detailLabel;
        private readonly ProgressBar _progressBar;

        public ProgressForm()
        {
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(0, 0);
            Size = new Size(430, 94);
            TopMost = true;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            Text = "SyncClipboardWin";

            _titleLabel = new Label();
            _titleLabel.AutoSize = false;
            _titleLabel.Location = new Point(10, 8);
            _titleLabel.Size = new Size(400, 20);
            _titleLabel.Text = "准备中...";

            _progressBar = new ProgressBar();
            _progressBar.Location = new Point(10, 31);
            _progressBar.Size = new Size(400, 18);
            _progressBar.Minimum = 0;
            _progressBar.Maximum = 100;
            _progressBar.Style = ProgressBarStyle.Continuous;

            _detailLabel = new Label();
            _detailLabel.AutoSize = false;
            _detailLabel.Location = new Point(10, 53);
            _detailLabel.Size = new Size(400, 18);

            Controls.Add(_titleLabel);
            Controls.Add(_progressBar);
            Controls.Add(_detailLabel);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Location = new Point(0, 0);
        }

        public void ShowPreparing(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(ShowPreparing), text);
                return;
            }

            _titleLabel.Text = text;
            _detailLabel.Text = "";
            _progressBar.Style = ProgressBarStyle.Marquee;
            _progressBar.MarqueeAnimationSpeed = 25;
        }

        public void UpdateProgress(TransferProgress progress)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<TransferProgress>(UpdateProgress), progress);
                return;
            }

            string op = string.IsNullOrWhiteSpace(progress.Operation) ? "传输" : progress.Operation;
            string item = progress.ItemName ?? "";
            _titleLabel.Text = string.IsNullOrWhiteSpace(item) ? op : op + "：" + item;

            int pct = progress.Percentage;
            if (pct < 0)
            {
                if (_progressBar.Style != ProgressBarStyle.Marquee)
                {
                    _progressBar.Style = ProgressBarStyle.Marquee;
                    _progressBar.MarqueeAnimationSpeed = 25;
                }
                _detailLabel.Text = FormatBytes(progress.TransferredBytes);
            }
            else
            {
                if (_progressBar.Style != ProgressBarStyle.Continuous)
                {
                    _progressBar.MarqueeAnimationSpeed = 0;
                    _progressBar.Style = ProgressBarStyle.Continuous;
                }
                _progressBar.Value = pct;
                _detailLabel.Text = string.Format("{0}%   {1} / {2}", pct, FormatBytes(progress.TransferredBytes), FormatBytes(progress.TotalBytes));
            }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            if (bytes >= 1024L * 1024L * 1024L) return string.Format("{0:F2} GB", bytes / 1024d / 1024d / 1024d);
            if (bytes >= 1024L * 1024L) return string.Format("{0:F2} MB", bytes / 1024d / 1024d);
            if (bytes >= 1024L) return string.Format("{0:F2} KB", bytes / 1024d);
            return bytes + " B";
        }
    }
}
