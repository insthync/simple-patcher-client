using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace SimplePatcher
{
    public class SimplePatcherClient : MonoBehaviour
    {
        [Serializable]
        public class StringEvent : UnityEvent<string> { }
        [Serializable]
        public class ProgressEvent : UnityEvent<long, long> { }
        [Serializable]
        public struct ValidateResult
        {
            public bool updated;
            public string fileurl;
            public string filemd5;
        }

        public enum State
        {
            None,
            ValidatingMD5,
            Downloading,
            Unzipping,
            ReadyToPlay
        }
        public string cachingMD5File = "downloading_md5.txt";
        public string unzippedMD5File = "unzipped_md5.txt";
        public string cachingZipFile = "local.zip";
        public string directoryPath = "Exe";
        public string pcExeFileName = "";
        public string macExeFileName = "";
        public string serviceUrl = "ENTER YOUR SERVICE URL HERE";
        public StringEvent onCompareMD5Error;
        public ProgressEvent onDownloadingProgress;
        public UnityEvent onDownloadingFileNotExisted;

        public State CurrentState { get; private set; }
        private bool destroyed;

        private void OnDestroy()
        {
            destroyed = true;
        }

        public void StartUpdate()
        {
            StartUpdateRoutine();
        }

        async void StartUpdateRoutine()
        {
            CurrentState = State.ValidatingMD5;
            string md5 = string.Empty;
            string unzippedMD5File = GetUnzippedMD5FilePath();
            if (File.Exists(unzippedMD5File))
            {
                using (StreamReader reader = new StreamReader(unzippedMD5File))
                {
                    md5 = await reader.ReadToEndAsync();
                    reader.Close();
                }
            }
            string validateUrl = serviceUrl + "?md5=" + md5;
            Debug.Log("Validating MD5: " + md5 + " URL: " + validateUrl);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(validateUrl);
            using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        string content = await reader.ReadToEndAsync();
                        Debug.Log("Validate MD5 Result: " + content);
                        ValidateResult result = JsonUtility.FromJson<ValidateResult>(content);
                        if (!result.updated)
                        {
                            CurrentState = State.Downloading;
                            await DownloadFileRoutine(result.fileurl, result.filemd5);
                            CurrentState = State.Unzipping;
                            await UnzipRoutine();
                        }
                        CurrentState = State.ReadyToPlay;
                        break;
                    default:
                        string description = response.StatusDescription;
                        Debug.LogError("Error occurs when validate MD5: " + description);
                        onCompareMD5Error.Invoke(description);
                        break;
                }
                reader.Close();
                stream.Close();
                response.Close();
            }
            request.Abort();
        }

        async Task DownloadFileRoutine(string url, string serviceMD5)
        {
            string cachingMD5 = string.Empty;
            string cachingMD5FilePath = GetCachingMD5FilePath();
            if (File.Exists(cachingMD5FilePath))
            {
                using (StreamReader reader = new StreamReader(cachingMD5FilePath))
                {
                    cachingMD5 = await reader.ReadToEndAsync();
                    reader.Close();
                }
            }
            string cachingFilePath = GetCachingFilePath();
            if (File.Exists(cachingFilePath) && !serviceMD5.Equals(cachingMD5))
            {
                // Clear old partial downloading file
                Debug.Log("Caching file with different MD5 exists, delete it");
                File.Delete(cachingFilePath);
            }
            cachingMD5 = serviceMD5;
            WriteMD5(cachingMD5FilePath, cachingMD5);
            long cachingFileSize;
            long downloadingFileSize;
            cachingFileSize = File.Exists(cachingFilePath) ? new FileInfo(cachingFilePath).Length : 0;
            // Get downloading file size
            HttpWebRequest headRequest = (HttpWebRequest)WebRequest.Create(url);
            headRequest.Method = WebRequestMethods.Http.Head;
            HttpWebResponse headResponse;
            try
            {
                headResponse = (HttpWebResponse)await headRequest.GetResponseAsync();
                headRequest.Abort();
            }
            catch
            {
                // Cannot find the file from service
                onDownloadingFileNotExisted.Invoke();
                return;
            }
            downloadingFileSize = headResponse.ContentLength;
            if (cachingFileSize != downloadingFileSize && !destroyed)
                await DownloadFile(cachingFilePath, url, cachingFileSize, downloadingFileSize);
            Debug.Log("Downloaded");
        }

        async Task DownloadFile(string cachingFilePath, string downloadingFileUrl, long cachingFileSize, long downloadingFileSize)
        {
            int bufferSize = 1024 * 1000;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(downloadingFileUrl);
            request.Timeout = 30000;
            request.AddRange(cachingFileSize, downloadingFileSize - 1);
            request.Method = WebRequestMethods.Http.Get;
            using (WebResponse response = await request.GetResponseAsync())
            using (Stream responseStream = response.GetResponseStream())
            using (FileStream writeFileStream = new FileStream(cachingFilePath, (cachingFileSize > 0) ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                int count;
                byte[] buff = new byte[bufferSize];
                while ((count = await responseStream.ReadAsync(buff, 0, bufferSize)) > 0)
                {
                    await writeFileStream.WriteAsync(buff, 0, count);
                    writeFileStream.Flush();
                    cachingFileSize += count;
                    Debug.Log("Downloading " + cachingFileSize + "/" + downloadingFileSize);
                    onDownloadingProgress.Invoke(cachingFileSize, downloadingFileSize);
                }

                writeFileStream.Flush();
                writeFileStream.Close();
                responseStream.Close();
                response.Close();
            }
            request.Abort();
        }

        async Task UnzipRoutine()
        {
            string unzippedMD5;
            using (MD5 md5 = MD5.Create())
            {
                using (FileStream stream = File.OpenRead(GetCachingFilePath()))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    unzippedMD5 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            string unzipDirectoryPath = GetUnzipDirectoryPath();
            if (!Directory.Exists(unzipDirectoryPath))
                Directory.CreateDirectory(unzipDirectoryPath);
            WriteMD5(GetUnzippedMD5FilePath(), unzippedMD5);
            // TODO: Implement this
            await Task.Yield();
        }

        void WriteMD5(string path, string md5)
        {
            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.Write(md5);
                writer.Close();
            }
        }

        string GetUnzippedMD5FilePath()
        {
            return Path.Combine(Path.GetFullPath("."), Path.GetFileName(unzippedMD5File));
        }

        string GetCachingMD5FilePath()
        {
            return Path.Combine(Path.GetFullPath("."), Path.GetFileName(cachingMD5File));
        }

        string GetCachingFilePath()
        {
            return Path.Combine(Path.GetFullPath("."), Path.GetFileName(cachingZipFile));
        }

        string GetUnzipDirectoryPath()
        {
            return Path.Combine(Path.GetFullPath("."), directoryPath);
        }
    }
}
