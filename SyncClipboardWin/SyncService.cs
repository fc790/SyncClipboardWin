using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SyncClipboardWin
{
    public sealed class SyncService
    {
        private readonly Func<AppSettings> _settingsProvider;

        public SyncService(Func<AppSettings> settingsProvider)
        {
            _settingsProvider = settingsProvider;
        }

        public async Task<string> UploadAsync(Action<TransferProgress> progress)
        {
            AppSettings settings = _settingsProvider();
            Validate(settings);

            string activeDir;
            List<string> selected;

            if (ExplorerHelper.TryGetActiveExplorer(
                out activeDir,
                out selected))
            {
                if (selected.Count > 0)
                    return await UploadPathsAsync(selected, settings, progress);

                if (string.Equals(
                    activeDir,
                    AppSettings.DesktopDirectory,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "当前在桌面，但没有读取到选中的文件/文件夹。");
                }
            }

            string selectedText =
                ClipboardHelper.TryGetSelectedTextWithCopyFallback();

            if (!string.IsNullOrWhiteSpace(selectedText))
                return await UploadTextAsync(selectedText, settings, progress);

            throw new InvalidOperationException(
                "未检测到选中的文件、文件夹或文本，已取消上传。");
        }

        public async Task<string> UploadClipboardOnlyAsync(Action<TransferProgress> progress)
        {
            AppSettings settings = _settingsProvider();
            Validate(settings);

            if (Clipboard.ContainsFileDropList())
            {
                List<string> files = Clipboard.GetFileDropList()
                    .Cast<string>()
                    .Where(ExistsOrDirectory)
                    .ToList();

                if (files.Count > 0)
                    return await UploadPathsAsync(files, settings, progress);
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                    return await UploadTextAsync(text, settings, progress);
            }

            throw new InvalidOperationException(
                "剪贴板中没有可上传的文本或文件。");
        }

        public async Task<string> DownloadAsync(Action<TransferProgress> progress)
        {
            AppSettings settings = _settingsProvider();
            Validate(settings);

            using (WebDavClient client = new WebDavClient(settings))
            {
                SyncClipboardModel meta =
                    await client.GetMetadataAsync();

                if (string.Equals(
                    meta.Type,
                    "Text",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ClipboardHelper.PasteText(meta.Text ?? "");
                    return string.Format(
                        "已粘贴文本（{0} 字符）",
                        (meta.Text ?? "").Length);
                }

                if (!meta.HasData ||
                    string.IsNullOrWhiteSpace(meta.DataName))
                {
                    throw new InvalidDataException(
                        "云端记录没有可下载的数据文件。");
                }

                string explorerDir;
                List<string> ignoredSelection;
                bool inExplorer =
                    ExplorerHelper.TryGetActiveExplorer(
                        out explorerDir,
                        out ignoredSelection) &&
                    !string.IsNullOrWhiteSpace(explorerDir);

                string targetDir =
                    inExplorer
                        ? explorerDir
                        : AppSettings.TempDirectory;

                Directory.CreateDirectory(targetDir);

                if (!inExplorer)
                    CleanupTempDirectory(settings.TempLimitBytes);

                string localPath = GetUniquePath(
                    Path.Combine(targetDir, meta.DataName));

                await client.DownloadFileAsync(
                    meta.DataName,
                    localPath,
                    progress);

                if (string.Equals(
                    meta.Type,
                    "Group",
                    StringComparison.OrdinalIgnoreCase))
                {
                    ExtractZipOverwrite(localPath, targetDir);
                    File.Delete(localPath);
                    return "已下载并解压到：" + targetDir;
                }

                if (!inExplorer)
                    ClipboardHelper.PasteFile(localPath);

                if (inExplorer)
                    return "已下载到：" + localPath;

                return "已下载并粘贴：" +
                       Path.GetFileName(localPath);
            }
        }

        public async Task TestConnectionAsync()
        {
            AppSettings settings = _settingsProvider();
            Validate(settings);

            using (WebDavClient client =
                new WebDavClient(settings))
            {
                await client.TestAsync();
            }
        }

        private async Task<string> UploadTextAsync(
            string text,
            AppSettings settings,
            Action<TransferProgress> progress)
        {
            SyncClipboardModel model =
                new SyncClipboardModel();

            model.Type = "Text";
            model.Hash = Sha256(
                Encoding.UTF8.GetBytes(text));
            model.Text = text;
            model.HasData = false;
            model.DataName = null;
            model.Size = text.Length;

            if (progress != null)
                progress(new TransferProgress(
                    "上传文本", "", 0, Math.Max(1, text.Length)));

            using (WebDavClient client =
                new WebDavClient(settings))
            {
                await client.PutMetadataAsync(model);
            }

            if (progress != null)
                progress(new TransferProgress(
                    "上传文本", "", Math.Max(1, text.Length), Math.Max(1, text.Length)));

            return string.Format(
                "已上传文本（{0} 字符）",
                text.Length);
        }

        private async Task<string> UploadPathsAsync(
            List<string> paths,
            AppSettings settings,
            Action<TransferProgress> progress)
        {
            string tempZip = null;
            string uploadPath;
            string remoteName;
            string displayText;
            string type;

            bool group =
                paths.Count > 1 ||
                Directory.Exists(paths[0]);

            if (group)
            {
                if (progress != null)
                    progress(new TransferProgress(
                        "准备压缩",
                        paths.Count > 1 ? paths.Count + " 个项目" : Path.GetFileName(paths[0]),
                        0,
                        -1));

                Directory.CreateDirectory(
                    AppSettings.TempDirectory);

                remoteName =
                    "SyncClipboard_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                    ".zip";

                tempZip = Path.Combine(
                    AppSettings.TempDirectory,
                    remoteName);

                CreateZip(paths, tempZip);
                uploadPath = tempZip;

                if (paths.Count > 1)
                {
                    displayText = string.Join(
                        Environment.NewLine,
                        paths.Select(
                            delegate(string p)
                            {
                                return Path.GetFileName(p);
                            })
                        .ToArray());
                }
                else
                {
                    displayText = Path.GetFileName(
                        paths[0].TrimEnd(
                            Path.DirectorySeparatorChar,
                            Path.AltDirectorySeparatorChar));
                }

                type = "Group";
            }
            else
            {
                uploadPath = paths[0];
                remoteName = Path.GetFileName(uploadPath);
                displayText = remoteName;
                type =
                    settings.UseImageType &&
                    IsImageFile(uploadPath)
                        ? "Image"
                        : "File";
            }

            try
            {
                long size =
                    new FileInfo(uploadPath).Length;

                if (size > settings.UploadLimitBytes)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            "文件大小 {0} 超过上传限制 {1}。",
                            FormatBytes(size),
                            FormatBytes(
                                settings.UploadLimitBytes)));
                }

                if (progress != null)
                    progress(new TransferProgress(
                        "计算校验", remoteName, 0, -1));

                string hash =
                    await Sha256FileAsync(uploadPath);

                SyncClipboardModel model =
                    new SyncClipboardModel();

                model.Type = type;
                model.Hash = hash;
                model.Text = displayText;
                model.HasData = true;
                model.DataName = remoteName;
                model.Size = size;

                using (WebDavClient client =
                    new WebDavClient(settings))
                {
                    await client.ResetFileDirectoryAsync();
                    await client.UploadFileAsync(
                        uploadPath,
                        remoteName,
                        progress);
                    await client.PutMetadataAsync(model);
                }

                return string.Format(
                    "已上传 {0}：{1}",
                    type,
                    displayText);
            }
            finally
            {
                if (tempZip != null)
                {
                    try { File.Delete(tempZip); }
                    catch { }
                }
            }
        }

        private static void CreateZip(
            List<string> paths,
            string zipPath)
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using (ZipArchive zip = ZipFile.Open(
                zipPath,
                ZipArchiveMode.Create))
            {
                foreach (string path in paths)
                {
                    if (File.Exists(path))
                    {
                        zip.CreateEntryFromFile(
                            path,
                            Path.GetFileName(path),
                            CompressionLevel.Fastest);
                    }
                    else if (Directory.Exists(path))
                    {
                        string baseName =
                            Path.GetFileName(
                                path.TrimEnd(
                                    Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar));

                        AddDirectory(
                            zip,
                            path,
                            baseName);
                    }
                }
            }
        }

        private static void AddDirectory(
            ZipArchive zip,
            string directory,
            string entryRoot)
        {
            string[] files =
                Directory.GetFiles(
                    directory,
                    "*",
                    SearchOption.AllDirectories);

            foreach (string file in files)
            {
                string rel =
                    MakeRelativePath(directory, file);

                string entryName =
                    Path.Combine(entryRoot, rel)
                    .Replace('\\', '/');

                zip.CreateEntryFromFile(
                    file,
                    entryName,
                    CompressionLevel.Fastest);
            }
        }

        private static string MakeRelativePath(
            string baseDirectory,
            string fullPath)
        {
            string basePath =
                Path.GetFullPath(baseDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            Uri baseUri = new Uri(basePath);
            Uri fileUri = new Uri(
                Path.GetFullPath(fullPath));

            return Uri.UnescapeDataString(
                baseUri.MakeRelativeUri(fileUri)
                .ToString())
                .Replace(
                    '/',
                    Path.DirectorySeparatorChar);
        }

        private static void ExtractZipOverwrite(
            string zipPath,
            string targetDirectory)
        {
            string root =
                Path.GetFullPath(targetDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;

            using (ZipArchive zip =
                ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string relative =
                        entry.FullName.Replace(
                            '/',
                            Path.DirectorySeparatorChar);

                    string destination =
                        Path.GetFullPath(
                            Path.Combine(
                                targetDirectory,
                                relative));

                    if (!destination.StartsWith(
                        root,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            "ZIP 中包含非法路径。");
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    string parent =
                        Path.GetDirectoryName(destination);

                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    entry.ExtractToFile(
                        destination,
                        true);
                }
            }
        }

        private static bool IsImageFile(string path)
        {
            string ext =
                (Path.GetExtension(path) ?? "")
                .ToLowerInvariant();

            string[] extensions = new string[]
            {
                ".jpg", ".jpeg", ".png", ".bmp",
                ".gif", ".webp", ".tif", ".tiff"
            };

            return extensions.Contains(ext);
        }

        private static async Task<string>
            Sha256FileAsync(string path)
        {
            byte[] hash;

            using (FileStream fs = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                hash = await Task.Run(
                    delegate
                    {
                        return sha.ComputeHash(fs);
                    });
            }

            return BytesToHex(hash);
        }

        private static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BytesToHex(
                    sha.ComputeHash(data));
            }
        }

        private static string BytesToHex(byte[] data)
        {
            StringBuilder sb =
                new StringBuilder(data.Length * 2);

            for (int i = 0; i < data.Length; i++)
                sb.Append(data[i].ToString("x2"));

            return sb.ToString();
        }

        private static void CleanupTempDirectory(
            long limit)
        {
            Directory.CreateDirectory(
                AppSettings.TempDirectory);

            List<FileInfo> files =
                new DirectoryInfo(
                    AppSettings.TempDirectory)
                .GetFiles(
                    "*",
                    SearchOption.AllDirectories)
                .OrderBy(
                    delegate(FileInfo f)
                    {
                        return f.LastWriteTimeUtc;
                    })
                .ToList();

            long total = files.Sum(
                delegate(FileInfo f)
                {
                    return f.Length;
                });

            if (total <= limit)
                return;

            foreach (FileInfo file in files)
            {
                try
                {
                    long len = file.Length;
                    file.Delete();
                    total -= len;

                    if (total <= limit)
                        break;
                }
                catch { }
            }
        }

        private static string GetUniquePath(
            string path)
        {
            if (!File.Exists(path) &&
                !Directory.Exists(path))
            {
                return path;
            }

            string dir =
                Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(dir))
                dir = AppSettings.TempDirectory;

            string name =
                Path.GetFileNameWithoutExtension(path);

            string ext =
                Path.GetExtension(path);

            for (int i = 1; i < 10000; i++)
            {
                string candidate =
                    Path.Combine(
                        dir,
                        name + " (" + i + ")" + ext);

                if (!File.Exists(candidate) &&
                    !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new IOException(
                "无法生成不重复的目标文件名。");
        }

        private static bool ExistsOrDirectory(
            string path)
        {
            return File.Exists(path) ||
                   Directory.Exists(path);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
                return string.Format(
                    "{0:F2} GB",
                    bytes / 1024d / 1024d / 1024d);

            if (bytes >= 1024L * 1024L)
                return string.Format(
                    "{0:F2} MB",
                    bytes / 1024d / 1024d);

            if (bytes >= 1024L)
                return string.Format(
                    "{0:F2} KB",
                    bytes / 1024d);

            return bytes + " B";
        }

        private static void Validate(
            AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(
                settings.WebDavUrl))
            {
                throw new InvalidOperationException(
                    "请先在设置中填写 WebDAV 地址。");
            }
        }
    }
}
