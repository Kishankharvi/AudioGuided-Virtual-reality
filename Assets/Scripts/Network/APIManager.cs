using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using AGVRSystem.Data;

namespace AGVRSystem.Network
{
    /// <summary>
    /// REST client using UnityWebRequest. POST sessions to server, retry offline cached sessions.
    /// </summary>
    public class APIManager : MonoBehaviour
    {
        [SerializeField] private string _baseUrl = "http://localhost:8000";

        private const float RequestTimeout = 10f;
        private const string OfflinePrefix = "offline_";
        private const string JsonExtension = ".json";
        private const long HttpCreated = 201;

        private void Start()
        {
            StartCoroutine(RetryOfflineSessions());
        }

        /// <summary>
        /// Posts session data to the server. Falls back to offline storage on failure.
        /// </summary>
        public void PostSession(SessionData data, Action<bool> onComplete = null)
        {
            if (data == null)
            {
                Debug.LogWarning("[APIManager] Attempted to post null session data.");
                onComplete?.Invoke(false);
                return;
            }

            StartCoroutine(PostSessionCoroutine(data, onComplete));
        }

        /// <summary>
        /// Scans persistentDataPath for offline session files and attempts to upload them.
        /// </summary>
        public IEnumerator RetryOfflineSessions()
        {
            string dataPath = Application.persistentDataPath;
            if (!Directory.Exists(dataPath))
                yield break;

            string[] offlineFiles = Directory.GetFiles(dataPath, OfflinePrefix + "*" + JsonExtension);

            foreach (string filePath in offlineFiles)
            {
                string json;
                try
                {
                    json = File.ReadAllText(filePath);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[APIManager] Failed to read offline file {filePath}: {e.Message}");
                    continue;
                }

                bool success = false;
                yield return PostJsonCoroutine(json, result => success = result);

                if (success)
                {
                    try
                    {
                        File.Delete(filePath);
                        Debug.Log($"[APIManager] Successfully synced and deleted offline file: {Path.GetFileName(filePath)}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[APIManager] Failed to delete offline file {filePath}: {e.Message}");
                    }
                }
            }
        }

        private IEnumerator PostSessionCoroutine(SessionData data, Action<bool> onComplete)
        {
            string json = data.ToJson();
            bool success = false;

            yield return PostJsonCoroutine(json, result => success = result);

            if (!success)
            {
                SaveOffline(data.sessionId, json);
            }

            onComplete?.Invoke(success);
        }

        private IEnumerator PostJsonCoroutine(string json, Action<bool> onComplete)
        {
            string url = _baseUrl + "/api/session";
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = (int)RequestTimeout;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success && request.responseCode == HttpCreated)
                {
                    Debug.Log("[APIManager] Session posted successfully.");
                    onComplete?.Invoke(true);
                }
                else
                {
                    Debug.LogWarning($"[APIManager] POST failed: {request.error} (HTTP {request.responseCode})");
                    onComplete?.Invoke(false);
                }
            }
        }

        private void SaveOffline(string sessionId, string json)
        {
            string fileName = OfflinePrefix + sessionId + JsonExtension;
            string filePath = Path.Combine(Application.persistentDataPath, fileName);

            try
            {
                File.WriteAllText(filePath, json);
                Debug.Log($"[APIManager] Session saved offline: {fileName}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[APIManager] Failed to save offline session: {e.Message}");
            }
        }
    }
}
