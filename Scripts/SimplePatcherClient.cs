using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace SimplePatcher
{
    public class SimplePatcherClient : MonoBehaviour
    {
        public class StringEvent : UnityEvent<string> { }
        [System.Serializable]
        public struct ValidateResult
        {
            public bool updated;
            public string fileurl;
        }
        
        public enum State
        {
            None,
            ValidatingMD5,
            Downloading,
            Unzipping,
            ReadyToPlay
        }
        public string saveLatestMD5File = "md5.txt";
        public string serviceUrl = "ENTER YOUR SERVICE URL HERE";
        public StringEvent onCompareMD5Error;

        public State CurrentState { get; private set; }

        public void StartUpdate()
        {
            string md5 = string.Empty;
            string combinedPath = Path.Combine(Application.dataPath, Path.GetFileName(saveLatestMD5File));
            if (File.Exists(combinedPath))
            {
                StreamReader reader = new StreamReader(combinedPath);
                md5 = reader.ReadToEnd();
                reader.Close();
            }
            StartCoroutine(StartUpdateRoutine(md5));
        }

        IEnumerator StartUpdateRoutine(string lastUpdateMD5)
        {
            CurrentState = State.ValidatingMD5;
            UnityWebRequest request = new UnityWebRequest(serviceUrl + "?md5=" + lastUpdateMD5);
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
                    yield return StartCoroutine(DownloadFileRoutine(result.fileurl));
                    CurrentState = State.Unzipping;
                    yield return StartCoroutine(UnzipRoutine());
                }
            }
            CurrentState = State.ReadyToPlay;
        }

        IEnumerator DownloadFileRoutine(string url)
        {
            // TODO: Implement this
            yield return null;
        }

        IEnumerator UnzipRoutine()
        {
            // TODO: Implement this
            yield return null;
        }
    }
}
