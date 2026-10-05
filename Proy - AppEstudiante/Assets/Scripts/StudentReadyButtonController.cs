using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class StudentReadyButtonController : MonoBehaviour
{
    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";
    [SerializeField] private float heartbeatSeconds = 3f;

    private Button readyButton;
    private TMP_Text buttonText;
    private bool isReady;
    private bool isSending;

    private void Awake()
    {
        readyButton = GetComponent<Button>();
        buttonText = GetComponentInChildren<TMP_Text>();

        if (readyButton == null)
        {
            Debug.LogError("Este script debe agregarse al GameObject del botón.");
            enabled = false;
            return;
        }

        readyButton.onClick.AddListener(ToggleReady);
        RefreshVisuals();
    }

    private void Start()
    {
        StartCoroutine(HeartbeatLoop());
    }

    private void OnDestroy()
    {
        if (readyButton != null)
            readyButton.onClick.RemoveListener(ToggleReady);
    }

    private void ToggleReady()
    {
        if (isSending || !HasStudentSession())
            return;

        StartCoroutine(SendReadyState(!isReady));
    }

    private IEnumerator SendReadyState(bool nextReadyState)
    {
        isSending = true;
        readyButton.interactable = false;

        string json = $"{{\"ready\":{nextReadyState.ToString().ToLower()}}}";
        string url = $"{GetBaseUrl()}/api/sessions/{StudentSessionContext.SessionId}/students/{StudentSessionContext.StudentId}/ready";

        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 8;

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            isReady = nextReadyState;
            RefreshVisuals();
        }
        else
        {
            Debug.LogWarning($"No se pudo actualizar Ready: {request.error}");
        }

        isSending = false;
        readyButton.interactable = true;
    }

    private IEnumerator HeartbeatLoop()
    {
        while (true)
        {
            if (HasStudentSession())
            {
                string url = $"{GetBaseUrl()}/api/sessions/{StudentSessionContext.SessionId}/students/{StudentSessionContext.StudentId}/heartbeat";

                using var request = new UnityWebRequest(url, "POST");
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 5;

                yield return request.SendWebRequest();
            }

            yield return new WaitForSecondsRealtime(heartbeatSeconds);
        }
    }

    private bool HasStudentSession()
    {
        return !string.IsNullOrWhiteSpace(StudentSessionContext.SessionId)
            && !string.IsNullOrWhiteSpace(StudentSessionContext.StudentId);
    }

    private string GetBaseUrl()
    {
        return PlayerPrefs
            .GetString("SerenaServerUrl", serverBaseUrl)
            .TrimEnd('/');
    }

    private void RefreshVisuals()
    {
        Color32 red = new Color32(200, 16, 46, 255);
        Color32 green = new Color32(16, 185, 129, 255);

        readyButton.image.color = isReady ? green : red;

        if (buttonText != null)
            buttonText.text = isReady ? "I'M READY" : "¿Ready?";
    }
}