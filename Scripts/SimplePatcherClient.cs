using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using ICSharpCode.SharpZipLib.Zip;
using System.Linq;

namespace SimplePatcher
{
    public class SimplePatcherClient : MonoBehaviour
    {
        public enum State
        {
            None,
            ValidatingMD5,
            Downloading,
            Unzipping,
            ReadyToPlay
        }
        [Serializable]
        public class StringEvent : UnityEvent<string> { }
        [Serializable]
        public class ProgressEvent : UnityEvent<long, long> { }
        [Serializable]
        public class StateEvent : UnityEvent<State> { }
        [Serializable]
        public struct ValidateResult
        {
            public bool updated;
            public string fileurl;
            public string filemd5;
            public string notice;
            public string windowsexe;
            public string osxexe;
        }

        public string cachingMD5File = "downloading_md5.txt";
        public string unzippedMD5File = "unzipped_md5.txt";
        public string cachingZipFile = "local.zip";
        public string unzipDirectory = "Exe";
        public string cacheDirectory = "Cache";
        public string serviceUrl = "ENTER YOUR SERVICE URL HERE";
        public StringEvent onReceiveNotice;
        public StringEvent onCompareMD5Error;
        public ProgressEvent onDownloadingProgress;
        public UnityEvent onDownloadingFileNotExisted;
        public ProgressEvent onUnzippingProgress;
        public StringEvent onUnzippingFileName;
        public ProgressEvent onUnzippingFileProgress;
        public StateEvent onStateChange;

        public State CurrentState { get; private set; }
        private ValidateResult result;
        private bool destroyed;

        private void OnDestroy()
        {
            destroyed = true;
        }

        public void StartUpdate()
        {
            StartUpdateRoutine();
        }

        public void PlayGame()
        {
            if (CurrentState != State.ReadyToPlay)
                return;
            string filePath = string.Empty;
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsPlayer:
                    filePath = Directory.GetFiles(GetUnzipDirectoryPath(), result.windowsexe, SearchOption.AllDirectories).FirstOrDefault();
                    break;
                case RuntimePlatform.OSXPlayer:
                    filePath = Directory.GetFiles(GetUnzipDirectoryPath(), result.osxexe, SearchOption.AllDirectories).FirstOrDefault();
                    break;
            }
            if (!string.IsNullOrEmpty(filePath))
            {
                System.Diagnostics.Process.Start(filePath);
            }
        }

        async void StartUpdateRoutine()
        {
            // Prepare directories
            string cacheDirectoryPath = GetCacheDirectoryPath();
            if (!Directory.Exists(cacheDirectoryPath))
                Directory.CreateDirectory(cacheDirectoryPath);
            string unzipDirectoryPath = GetUnzipDirectoryPath();
            if (!Directory.Exists(unzipDirectoryPath))
                Directory.CreateDirectory(unzipDirectoryPath);

            CurrentState = State.ValidatingMD5;
            onStateChange.Invoke(CurrentState);
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
                        result = JsonUtility.FromJson<ValidateResult>(content);
                        onReceiveNotice.Invoke(result.notice);
                        if (!result.updated)
                        {
                            CurrentState = State.Downloading;
                            onStateChange.Invoke(CurrentState);
                            await DownloadFileRoutine(result.fileurl, result.filemd5);
                            CurrentState = State.Unzipping;
                            onStateChange.Invoke(CurrentState);
                            await UnzipRoutine();
                        }
                        CurrentState = State.ReadyToPlay;
                        onStateChange.Invoke(CurrentState);
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
            if (cachingFileSize != downloadingFileSize)
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
                    Debug.Log("Downloading: " + cachingFileSize + "/" + downloadingFileSize);
                    onDownloadingProgress.Invoke(cachingFileSize, downloadingFileSize);
                    if (destroyed)
                        break;
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
            string cachingFilePath = GetCachingFilePath();
            using (FileStream stream = File.OpenRead(cachingFilePath))
            {
                await UnZip(stream, GetUnzipDirectoryPath());
            }
            string unzippedMD5;
            using (MD5 md5 = MD5.Create())
            using (FileStream stream = File.OpenRead(cachingFilePath))
            {
                byte[] hash = md5.ComputeHash(stream);
                unzippedMD5 = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                stream.Close();
            }
            Debug.Log("UnZipped");
            WriteMD5(GetUnzippedMD5FilePath(), unzippedMD5);
            Debug.Log("Written MD5: " + unzippedMD5);
            File.Delete(cachingFilePath);
        }

        void WriteMD5(string path, string md5)
        {
            using (StreamWriter writer = new StreamWriter(path))
            {
                writer.Write(md5);
                writer.Close();
            }
        }

        async Task UnZip(Stream zipStream, string extractDirectory, string password = "")
        {
            using (ZipInputStream zipInputStream = new ZipInputStream(zipStream))
            {
                // Set a zip password if it's required
                if (password.Length > 0)
                {
                    zipInputStream.Password = password;
                }
                ZipEntry zipEntry = null;
                while ((zipEntry = zipInputStream.GetNextEntry()) != null)
                {
                    Debug.Log("Unzipping: " + zipStream.Position + "/" + zipStream.Length);
                    onUnzippingProgress.Invoke(zipStream.Position, zipStream.Length);
                    Debug.Log("Unzipping Entry: " + zipEntry.Name);
                    onUnzippingFileName.Invoke(zipEntry.Name);
                    if (!zipEntry.IsFile)
                    {
                        string directoryName = Path.Combine(extractDirectory, Path.GetDirectoryName(zipEntry.Name));
                        // Create directory
                        if (directoryName.Length > 0 && !Directory.Exists(directoryName))
                        {
                            Directory.CreateDirectory(directoryName);
                        }
                    }
                    else
                    {
                        string extractPath = Path.Combine(extractDirectory, zipEntry.Name);
                        int totalCount = 0;
                        int count;
                        int bufferSize = 1024 * 1000;
                        byte[] buff = new byte[bufferSize];
                        using (FileStream writeFileStream = File.Create(extractPath))
                        {
                            while ((count = await zipInputStream.ReadAsync(buff, 0, bufferSize)) > 0)
                            {
                                totalCount += count;
                                await writeFileStream.WriteAsync(buff, 0, count);
                                writeFileStream.Flush();
                                Debug.Log("Unzipping Entry: " + totalCount + "/" + zipInputStream.Length);
                                onUnzippingFileProgress.Invoke(totalCount, zipInputStream.Length);
                                if (destroyed)
                                    break;
                            }
                            writeFileStream.Flush();
                            writeFileStream.Close();
                        }
                    }
                    if (destroyed)
                        break;
                }
                zipInputStream.Close();
            }
        }

        string GetUnzippedMD5FilePath()
        {
            return Path.Combine(GetCacheDirectoryPath(), Path.GetFileName(unzippedMD5File));
        }

        string GetCachingMD5FilePath()
        {
            return Path.Combine(GetCacheDirectoryPath(), Path.GetFileName(cachingMD5File));
        }

        string GetCachingFilePath()
        {
            return Path.Combine(GetCacheDirectoryPath(), Path.GetFileName(cachingZipFile));
        }

        string GetCacheDirectoryPath()
        {
            return Path.Combine(Path.GetFullPath("."), cacheDirectory);
        }

        string GetUnzipDirectoryPath()
        {
            return Path.Combine(Path.GetFullPath("."), unzipDirectory);
        }
    }
}
