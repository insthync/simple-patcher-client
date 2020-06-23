using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SimplePatcher
{
    public class UISimplePatcherClient : MonoBehaviour
    {
        public Text textNotice;
        public string formatDownloadProgress = "Downloading... {0}/{1}";
        public Text textDownloadProgress;
        public Image imageDownloadProgress;
        public string formatUnzipProgress = "Extracting... {0}/{1}";
        public Text textUnzipProgress;
        public Image imageUnzipProgress;
        public string formatUnzipEntryProgress = "{0}/{1}";
        public Text textUnzipEntryProgress;
        public Image imageUnzipEntryProgress;
        public string formatUnzipName = "Extracting... {0}";
        public Text textUnzipEntryName;
        public Button playButton;

        SimplePatcherClient client;
        private void Start()
        {
            client = GetComponent<SimplePatcherClient>();
            client.onReceiveNotice.AddListener(OnReceiveNotice);
            client.onDownloadingProgress.AddListener(OnDownloadProgress);
            client.onUnzippingProgress.AddListener(OnUnzipProgress);
            client.onUnzippingFileProgress.AddListener(OnUnzipFileProgress);
            client.onUnzippingFileName.AddListener(OnUnzipEntryFileName);
            client.onStateChange.AddListener(OnStateChagne);
        }

        private void OnDestroy()
        {
            if (!client)
                return;
            client.onReceiveNotice.RemoveListener(OnReceiveNotice);
            client.onDownloadingProgress.RemoveListener(OnDownloadProgress);
            client.onUnzippingProgress.RemoveListener(OnUnzipProgress);
            client.onUnzippingFileProgress.RemoveListener(OnUnzipFileProgress);
            client.onUnzippingFileName.RemoveListener(OnUnzipEntryFileName);
            client.onStateChange.RemoveListener(OnStateChagne);
        }

        public void OnReceiveNotice(string notice)
        {
            if (textNotice != null)
                textNotice.text = notice;
        }

        public void OnDownloadProgress(long current, long total)
        {
            if (textDownloadProgress != null)
                textDownloadProgress.text = string.Format(formatDownloadProgress, current, total);
            if (imageDownloadProgress != null)
                imageDownloadProgress.fillAmount = (float)((double)current / (double)total);
        }

        public void OnUnzipProgress(long current, long total)
        {
            if (textUnzipProgress != null)
                textUnzipProgress.text = string.Format(formatUnzipProgress, current, total);
            if (imageUnzipProgress != null)
                imageUnzipProgress.fillAmount = (float)((double)current / (double)total);
        }

        public void OnUnzipFileProgress(long current, long total)
        {
            if (textUnzipEntryProgress != null)
                textUnzipEntryProgress.text = string.Format(formatUnzipEntryProgress, current, total);
            if (imageUnzipEntryProgress != null)
                imageUnzipEntryProgress.fillAmount = (float)((double)current / (double)total);
        }

        public void OnUnzipEntryFileName(string name)
        {
            if (textUnzipEntryName != null)
                textUnzipEntryName.text = string.Format(formatUnzipName, name);
        }

        public void OnStateChagne(SimplePatcherClient.State state)
        {
            playButton.interactable = state == SimplePatcherClient.State.ReadyToPlay;
        }
    }
}
