using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class StudentReadyButtonController : MonoBehaviour
{

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

        StudentButtonDepthStyle.Apply(readyButton);
        readyButton.onClick.AddListener(ToggleReady);
        RefreshVisuals();
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
        string url = $"{SerenaServerConfig.BaseUrl}/api/sessions/{StudentSessionContext.SessionId}/students/{StudentSessionContext.StudentId}/ready";

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

    private bool HasStudentSession()
    {
        return !string.IsNullOrWhiteSpace(StudentSessionContext.SessionId)
            && !string.IsNullOrWhiteSpace(StudentSessionContext.StudentId);
    }

    private void RefreshVisuals()
    {
        Color32 red = new Color32(200, 16, 46, 255);
        Color32 green = new Color32(16, 185, 129, 255);

        readyButton.image.color = isReady ? green : red;

        if (buttonText != null)
            buttonText.text = isReady ? "Estoy listo/a" : "¿Estás listo/a?";
    }
}
