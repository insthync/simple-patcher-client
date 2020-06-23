using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace SimplePatcher
{
    public class SimplePatcherClient : MonoBehaviour
    {
        public class StringEvent : UnityEvent<string> { }
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
        public string serviceUrl = "ENTER YOUR SERVICE URL HERE";
        public StringEvent onCompareMD5Error;
        public ProgressEvent onDownloadingProgress;
        public UnityEvent onDownloadingFileNotExisted;

        public State CurrentState { get; private set; }

        public void StartUpdate()
        {
            StartCoroutine(StartUpdateRoutine());
        }

        IEnumerator StartUpdateRoutine()
        {
            CurrentState = State.ValidatingMD5;
            string md5 = string.Empty;
            string unzippedMD5File = GetUnzippedMD5FilePath();
            if (File.Exists(unzippedMD5File))
            {
                using (StreamReader reader = new StreamReader(unzippedMD5File))
                {
                    md5 = reader.ReadToEnd();
                    reader.Close();
                }
            }
            UnityWebRequest request = new UnityWebRequest(serviceUrl + "?md5=" + md5);
            yield return request.SendWebRequest();
            if (request.isHttpError || request.isNetworkError)
            {
                onCompareMD5Error.Invoke(request.error);
            }
            else
            {
                ValidateResult result = JsonUtility.FromJson<ValidateResult>(request.downloadHandler.text);
                if (!result.updated)
                {
                    CurrentState = State.Downloading;
                    yield return StartCoroutine(DownloadFileRoutine(result.fileurl, result.filemd5));
                    CurrentState = State.Unzipping;
                    yield return StartCoroutine(UnzipRoutine());
                }
            }
            CurrentState = State.ReadyToPlay;
        }

        IEnumerator DownloadFileRoutine(string url, string serviceMD5)
        {
            string cachingMD5 = string.Empty;
            string cachingMD5File = GetCachingMD5FilePath();
            if (File.Exists(cachingMD5File))
            {
                using (StreamReader reader = new StreamReader(cachingMD5File))
                {
                    cachingMD5 = reader.ReadToEnd();
                    reader.Close();
                }
            }
            string cachingFilePath = GetCachingFilePath();
            bool cachingFileExists = File.Exists(cachingFilePath);
            if (cachingFileExists && !serviceMD5.Equals(cachingMD5))
            {
                // Clear old partial downloading file
                File.Delete(cachingFilePath);
                cachingFileExists = false;
            }
            long cachingFileSize;
            long downloadingFileSize;
            do
            {
                cachingFileSize = cachingFileExists ? new FileInfo(cachingFilePath).Length : 0;
                // Get downloading file size
                HttpWebRequest headRequest = (HttpWebRequest)WebRequest.Create(url);
                headRequest.Method = WebRequestMethods.Http.Head;
                HttpWebResponse headResponse;
                try
                {
                    headResponse = (HttpWebResponse)headRequest.GetResponse();
                    headRequest.Abort();
                }
                catch
                {
                    // Cannot find the file from service
                    onDownloadingFileNotExisted.Invoke();
                    yield break;
                }
                downloadingFileSize = headResponse.ContentLength;
                yield return null;
                onDownloadingProgress.Invoke(cachingFileSize, downloadingFileSize);
                if (cachingFileSize != downloadingFileSize)
                    DownloadFile(cachingFilePath, url, cachingFileSize, downloadingFileSize);
            } while (cachingFileSize != downloadingFileSize);
        }

        void DownloadFile(string cachingFilePath, string downloadingFileUrl, long cachingFileSize, long downloadingFileSize)
        {
            int bufferSize = 1024 * 1000;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(downloadingFileUrl);
            request.Timeout = 30000;
            request.AddRange((int)cachingFileSize, (int)downloadingFileSize - 1);
            request.Method = WebRequestMethods.Http.Get;
            WebResponse response = request.GetResponse();
            Stream inStream = response.GetResponseStream();
            FileStream outStream = new FileStream(cachingFilePath, (cachingFileSize > 0) ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

            int count;
            byte[] buff = new byte[bufferSize];
            while ((count = inStream.Read(buff, 0, bufferSize)) > 0)
            {
                outStream.Write(buff, 0, count);
                outStream.Flush();
            }

            outStream.Flush();
            outStream.Close();
            inStream.Close();
            request.Abort();
        }

        IEnumerator UnzipRoutine()
        {
            // TODO: Implement this
            yield return null;
        }

        string GetUnzippedMD5FilePath()
        {
            return Path.Combine(Application.dataPath, Path.GetFileName(unzippedMD5File));
        }

        string GetCachingMD5FilePath()
        {
            return Path.Combine(Application.dataPath, Path.GetFileName(cachingMD5File));
        }

        string GetCachingFilePath()
        {
            return Path.Combine(Application.dataPath, Path.GetFileName(cachingZipFile));
        }
    }
}
