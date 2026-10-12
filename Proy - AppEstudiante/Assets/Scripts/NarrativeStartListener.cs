using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class NarrativeStartListener : MonoBehaviour
{
    [Serializable]
    private class SessionStatus
    {
        public bool narrativeStarted;
    }

    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";
    [SerializeField] private string narrativeSceneName = "Narrativa1";
    [SerializeField] private float checkSeconds = 1f;

    private bool loadingScene;

    private void Start()
    {
        StartCoroutine(WaitForNarrativeStart());
    }

    private IEnumerator WaitForNarrativeStart()
    {
        while (!loadingScene)
        {
            if (!string.IsNullOrWhiteSpace(StudentSessionContext.SessionId))
            {
                string baseUrl = PlayerPrefs
                    .GetString("SerenaServerUrl", serverBaseUrl)
                    .TrimEnd('/');

                string url = $"{baseUrl}/api/sessions/{StudentSessionContext.SessionId}";

                using var request = UnityWebRequest.Get(url);
                request.timeout = 5;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var status = JsonUtility.FromJson<SessionStatus>(
                        request.downloadHandler.text
                    );

                    if (status != null && status.narrativeStarted)
                    {
                        loadingScene = true;
                        SceneManager.LoadScene(narrativeSceneName);
                        yield break;
                    }
                }
            }

            yield return new WaitForSecondsRealtime(checkSeconds);
        }
    }
}