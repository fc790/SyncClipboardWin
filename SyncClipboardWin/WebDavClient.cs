using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SyncClipboardWin
{
    public sealed class WebDavClient : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly string _authHeader;

        public WebDavClient(AppSettings settings)
        {
            _settings = settings;
            string raw = settings.Username + ":" + settings.Password;
            _authHeader = "Basic " +
                Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        }

        private string Root
        {
            get { return (_settings.WebDavUrl ?? "").TrimEnd('/'); }
        }

        private HttpWebRequest CreateRequest(string url, string method)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.AllowAutoRedirect = true;
            req.Timeout = 100000;
            req.ReadWriteTimeout = 100000;
            req.UserAgent = "SyncClipboardWin/0.3.2";
            req.Headers[HttpRequestHeader.Authorization] = _authHeader;
            req.Headers["Depth"] = "1";
            return req;
        }

        public async Task TestAsync()
        {
            HttpWebRequest req = CreateRequest(
                Root + "/SyncClipboard.json",
                "GET");

            try
            {
                using (HttpWebResponse res =
                    (HttpWebResponse)await req.GetResponseAsync())
                {
                    EnsureSuccess(res);
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null &&
                    response.StatusCode == HttpStatusCode.NotFound)
                {
                    response.Close();
                    return;
                }

                throw BuildWebDavException(ex);
            }
        }

        public async Task<SyncClipboardModel> GetMetadataAsync()
        {
            HttpWebRequest req = CreateRequest(
                Root + "/SyncClipboard.json",
                "GET");

            try
            {
                using (HttpWebResponse res =
                    (HttpWebResponse)await req.GetResponseAsync())
                {
                    EnsureSuccess(res);

                    string json;
                    using (StreamReader reader =
                        new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                    {
                        json = await reader.ReadToEndAsync();
                    }

                    return ParseMetadata(json);
                }
            }
            catch (WebException ex)
            {
                throw BuildWebDavException(ex);
            }
        }

        public async Task PutMetadataAsync(SyncClipboardModel model)
        {
            Dictionary<string, object> value =
                new Dictionary<string, object>();

            value["type"] = model.Type;
            value["hash"] = model.Hash;
            value["text"] = model.Text;
            value["hasData"] = model.HasData;
            value["dataName"] = model.DataName;
            value["size"] = model.Size;

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            byte[] data = Encoding.UTF8.GetBytes(serializer.Serialize(value));

            HttpWebRequest req = CreateRequest(
                Root + "/SyncClipboard.json",
                "PUT");

            req.ContentType = "application/json; charset=utf-8";
            req.ContentLength = data.Length;

            try
            {
                using (Stream requestStream = await req.GetRequestStreamAsync())
                {
                    await requestStream.WriteAsync(data, 0, data.Length);
                }

                using (HttpWebResponse res =
                    (HttpWebResponse)await req.GetResponseAsync())
                {
                    EnsureSuccess(res);
                }
            }
            catch (WebException ex)
            {
                throw BuildWebDavException(ex);
            }
        }

        public async Task<DirectTransferInfo> GetDirectInfoAsync()
        {
            HttpWebRequest req = CreateRequest(Root + "/SyncClipboard.direct.json", "GET");
            try
            {
                using (HttpWebResponse res = (HttpWebResponse)await req.GetResponseAsync())
                {
                    EnsureSuccess(res);
                    string json;
                    using (StreamReader reader = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                        json = await reader.ReadToEndAsync();
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    return serializer.Deserialize<DirectTransferInfo>(json);
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null && response.StatusCode == HttpStatusCode.NotFound)
                {
                    response.Close();
                    return null;
                }
                throw BuildWebDavException(ex);
            }
        }

        public async Task PutDirectInfoAsync(DirectTransferInfo info)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            byte[] data = Encoding.UTF8.GetBytes(serializer.Serialize(info));
            HttpWebRequest req = CreateRequest(Root + "/SyncClipboard.direct.json", "PUT");
            req.ContentType = "application/json; charset=utf-8";
            req.ContentLength = data.Length;
            try
            {
                using (Stream requestStream = await req.GetRequestStreamAsync())
                    await requestStream.WriteAsync(data, 0, data.Length);
                using (HttpWebResponse res = (HttpWebResponse)await req.GetResponseAsync())
                    EnsureSuccess(res);
            }
            catch (WebException ex) { throw BuildWebDavException(ex); }
        }

        public async Task ResetFileDirectoryAsync()
        {
            HttpWebRequest del = CreateRequest(Root + "/file/", "DELETE");
            try
            {
                using (HttpWebResponse res =
                    (HttpWebResponse)await del.GetResponseAsync())
                {
                }
            }
            catch
            {
                // Ignore DELETE failure; MKCOL/PUT below is authoritative.
            }

            HttpWebRequest mkcol = CreateRequest(Root + "/file/", "MKCOL");
            try
            {
                using (HttpWebResponse res =
                    (HttpWebResponse)await mkcol.GetResponseAsync())
                {
                    int code = (int)res.StatusCode;
                    if ((code >= 200 && code <= 299) ||
                        res.StatusCode == HttpStatusCode.MethodNotAllowed ||
                        res.StatusCode == HttpStatusCode.Conflict)
                    {
                        return;
                    }
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                if (response != null)
                {
                    HttpStatusCode code = response.StatusCode;
                    response.Close();

                    if (code == HttpStatusCode.MethodNotAllowed ||
                        code == HttpStatusCode.Conflict)
                    {
                        return;
                    }
                }

                throw BuildWebDavException(ex);
            }
        }

        public async Task UploadFileAsync(string localPath, string remoteName, Action<TransferProgress> progress, Func<bool> cancelled)
        {
            string url = Root + "/file/" + Uri.EscapeDataString(remoteName);
            FileInfo info = new FileInfo(localPath);

            HttpWebRequest req = CreateRequest(url, "PUT");
            req.ContentType = "application/octet-stream";
            req.ContentLength = info.Length;
            req.SendChunked = false;

            try
            {
                using (FileStream input = new FileStream(
                    localPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read))
                using (Stream output = await req.GetRequestStreamAsync())
                {
                    byte[] buffer = new byte[128 * 1024];
                    int read;
                    long transferred = 0;

                    if (progress != null)
                        progress(new TransferProgress(
                            "上传", remoteName, 0, info.Length));

                    while ((read = await input.ReadAsync(
                        buffer, 0, buffer.Length)) > 0)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        await output.WriteAsync(buffer, 0, read);
                        transferred += read;

                        if (progress != null)
                            progress(new TransferProgress(
                                "上传", remoteName, transferred, info.Length));
                    }
                }

                using (HttpWebResponse res =
                    (HttpWebResponse)await req.GetResponseAsync())
                {
                    EnsureSuccess(res);
                }
            }
            catch (WebException ex)
            {
                throw BuildWebDavException(ex);
            }
        }

        public async Task DownloadFileAsync(
            string remoteName,
            string localPath,
            Action<TransferProgress> progress,
            Func<bool> cancelled)
        {
            string url = Root + "/file/" + Uri.EscapeDataString(remoteName);
            HttpWebRequest req = CreateRequest(url, "GET");

            string dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            try
            {
                using (HttpWebResponse res =
                    (HttpWebResponse)await req.GetResponseAsync())
                {
                    EnsureSuccess(res);

                    using (Stream input = res.GetResponseStream())
                    using (FileStream output = new FileStream(
                        localPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        long total = res.ContentLength;
                        long transferred = 0;
                        byte[] buffer = new byte[128 * 1024];
                        int read;

                        if (progress != null)
                            progress(new TransferProgress(
                                "下载", remoteName, 0, total));

                        while ((read = await input.ReadAsync(
                            buffer, 0, buffer.Length)) > 0)
                        {
                            if (cancelled != null && cancelled()) throw new OperationCanceledException();
                            await output.WriteAsync(buffer, 0, read);
                            transferred += read;

                            if (progress != null)
                                progress(new TransferProgress(
                                    "下载", remoteName, transferred, total));
                        }
                    }
                }
            }
            catch (WebException ex)
            {
                throw BuildWebDavException(ex);
            }
        }

        private static SyncClipboardModel ParseMetadata(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            object raw = serializer.DeserializeObject(json);

            Dictionary<string, object> dict =
                raw as Dictionary<string, object>;

            if (dict == null)
                throw new InvalidDataException(
                    "SyncClipboard.json 内容无效。");

            SyncClipboardModel model = new SyncClipboardModel();
            model.Type = GetString(dict, "type");
            model.Hash = GetString(dict, "hash");
            model.Text = GetString(dict, "text");
            model.HasData = GetBool(dict, "hasData");
            model.DataName = GetNullableString(dict, "dataName");
            model.Size = GetLong(dict, "size");

            if (string.IsNullOrEmpty(model.Type))
                throw new InvalidDataException(
                    "SyncClipboard.json 缺少 type。");

            return model;
        }

        private static string GetString(
            Dictionary<string, object> dict,
            string key)
        {
            object value;
            if (!dict.TryGetValue(key, out value) || value == null)
                return "";
            return Convert.ToString(value);
        }

        private static string GetNullableString(
            Dictionary<string, object> dict,
            string key)
        {
            object value;
            if (!dict.TryGetValue(key, out value) || value == null)
                return null;
            return Convert.ToString(value);
        }

        private static bool GetBool(
            Dictionary<string, object> dict,
            string key)
        {
            object value;
            if (!dict.TryGetValue(key, out value) || value == null)
                return false;
            return Convert.ToBoolean(value);
        }

        private static long GetLong(
            Dictionary<string, object> dict,
            string key)
        {
            object value;
            if (!dict.TryGetValue(key, out value) || value == null)
                return 0L;
            return Convert.ToInt64(value);
        }

        private static void EnsureSuccess(HttpWebResponse response)
        {
            int code = (int)response.StatusCode;
            if (code < 200 || code > 299)
            {
                throw new WebException(
                    "WebDAV HTTP " + code + " " +
                    response.StatusDescription);
            }
        }

        private static Exception BuildWebDavException(WebException ex)
        {
            HttpWebResponse response = ex.Response as HttpWebResponse;
            if (response == null)
                return ex;

            int code = (int)response.StatusCode;
            string description = response.StatusDescription;
            response.Close();

            return new WebException(
                "WebDAV HTTP " + code + " " + description,
                ex);
        }

        public void Dispose()
        {
            // HttpWebRequest has no shared client object to dispose.
        }
    }
}
