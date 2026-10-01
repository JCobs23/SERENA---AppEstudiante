using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Holds the student identity for the current app session. The session code can
/// be supplied by the session-join flow when that screen is implemented.
/// </summary>
public static class StudentSessionContext
{
    public static string StudentName { get; private set; }
    public static string StudentId { get; private set; }
    public static string SessionId { get; private set; }

    public static void SetSessionId(string sessionId)
    {
        SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
    }

    public static void SetStudent(string name, string studentId)
    {
        StudentName = name;
        StudentId = studentId;
    }
}

public class StudentNameController : MonoBehaviour
{
    private const int MinNameLength = 2;
    private const int MaxNameLength = 60;

    [Header("UI References")]
    [SerializeField] private TMP_InputField inputFieldName;
    [SerializeField] private Button continueButton;
    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";

    [Header("Panel Navigation")]
    [SerializeField] private GameObject currentPanel;
    [SerializeField] private GameObject nextPanel;

    private string studentName = string.Empty;
    private string originalPlaceholder;

    private void Start()
    {
        if (inputFieldName != null)
        {
            inputFieldName.characterLimit = MaxNameLength;
            inputFieldName.onValueChanged.AddListener(ValidateInput);
            if (inputFieldName.placeholder is TMP_Text placeholder)
                originalPlaceholder = placeholder.text;
            ValidateInput(inputFieldName.text);
        }

        if (continueButton != null)
        {
            continueButton.interactable = false;
            continueButton.onClick.AddListener(OnContinuePressed);
        }
    }

    private void OnDestroy()
    {
        if (inputFieldName != null)
            inputFieldName.onValueChanged.RemoveListener(ValidateInput);

        if (continueButton != null)
            continueButton.onClick.RemoveListener(OnContinuePressed);
    }

    private void ValidateInput(string input)
    {
        studentName = (input ?? string.Empty).Trim();
        bool isValid = studentName.Length >= MinNameLength && studentName.Length <= MaxNameLength;

        if (continueButton != null)
            continueButton.interactable = isValid;
    }

    private void OnContinuePressed()
    {
        ValidateInput(inputFieldName != null ? inputFieldName.text : studentName);
        if (studentName.Length < MinNameLength || studentName.Length > MaxNameLength)
            return;

        if (continueButton != null) continueButton.interactable = false;
        StartCoroutine(RegisterStudentAndContinue());
    }

    private IEnumerator RegisterStudentAndContinue()
    {
        string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
        var payload = new JoinStudentRequest { name = studentName };
        byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        using var request = new UnityWebRequest($"{baseUrl}/api/sessions/{StudentSessionContext.SessionId}/students", "POST");
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 8;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            ShowConnectionMessage(request.responseCode == 404
                ? "La sesión expiró. Ingresa nuevamente el código."
                : "No se pudo conectar con Serena. Revisa la red y vuelve a intentar.");
            if (continueButton != null) continueButton.interactable = true;
            yield break;
        }

        var response = JsonUtility.FromJson<JoinStudentResponse>(request.downloadHandler.text);
        if (response == null || string.IsNullOrWhiteSpace(response.studentId))
        {
            ShowConnectionMessage("El servidor respondió con un dato inválido. Intenta nuevamente.");
            if (continueButton != null) continueButton.interactable = true;
            yield break;
        }

        ContinueToActivity(response.studentId);
    }

    private void ContinueToActivity(string studentId)
    {
        StudentSessionContext.SetStudent(studentName, studentId);
        SaveStudentData(studentName, studentId, StudentSessionContext.SessionId);

        if (currentPanel != null) currentPanel.SetActive(false);
        if (nextPanel != null) nextPanel.SetActive(true);
    }

    private void ShowConnectionMessage(string message)
    {
        if (inputFieldName != null && inputFieldName.placeholder is TMP_Text placeholder)
            placeholder.text = message;
        else
            Debug.LogWarning(message);
    }

    private static void SaveStudentData(string name, string studentId, string sessionId)
    {
        string folderPath = Application.persistentDataPath;
        string fullFilePath = Path.Combine(folderPath, "student_data.json");
        var data = new StudentData
        {
            studentName = name,
            studentId = studentId,
            sessionId = sessionId
        };

        try
        {
            Directory.CreateDirectory(folderPath);
            File.WriteAllText(fullFilePath, JsonUtility.ToJson(data, true));
            Debug.Log("Datos de la sesión del estudiante guardados localmente.");
        }
        catch (Exception exception)
        {
            Debug.LogError("No se pudieron guardar los datos del estudiante: " + exception.Message);
        }
    }
}

[Serializable]
public class JoinStudentRequest
{
    public string name;
}

[Serializable]
public class JoinStudentResponse
{
    public string studentId;
    public string sessionCode;
    public bool joined;
}

[Serializable]
public class StudentData
{
    public string studentName;
    public string studentId;
    public string sessionId;
}
