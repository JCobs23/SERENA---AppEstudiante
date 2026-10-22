using System;
using System.Collections;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Holds the student identity for the current app session. The session code can
/// be supplied by the session-join flow when that screen is implemented.
/// </summary>
public static class StudentSessionContext
{
    public static string StudentName { get; private set; }
    public static string StudentId { get; private set; }
    public static string SessionId { get; private set; }
    public static bool NarrativeStarted { get; private set; }
    public static bool NarrativeIntroCompleted { get; private set; }
    public static int CurrentNarrativeStage { get; private set; }
    public static string CurrentNarrativeStageLabel { get; private set; }
    public static string CurrentNarrativeStageDescription { get; private set; }
    public static bool VotingOpen { get; private set; }
    public static int ContinueVotes { get; private set; }
    public static int AlternativeVotes { get; private set; }
    public static string AdvanceMode { get; private set; }
    public static string FinalDecision { get; private set; }
    public static int VoteRound { get; private set; }

    public static void SetSessionId(string sessionId)
    {
        SessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
        NarrativeStarted = false;
        NarrativeIntroCompleted = false;
        CurrentNarrativeStage = 0;
        VotingOpen = false;
        ContinueVotes = 0;
        AlternativeVotes = 0;
        AdvanceMode = "automatic";
        FinalDecision = string.Empty;
        VoteRound = 0;
    }

    public static void ClearSession()
    {
        SessionId = null;
        StudentId = null;
        NarrativeStarted = false;
        NarrativeIntroCompleted = false;
        CurrentNarrativeStage = 0;
        CurrentNarrativeStageLabel = string.Empty;
        CurrentNarrativeStageDescription = string.Empty;
        VotingOpen = false;
        ContinueVotes = 0;
        AlternativeVotes = 0;
        AdvanceMode = "automatic";
        FinalDecision = string.Empty;
        VoteRound = 0;
    }


    public static void SetStudent(string name, string studentId)
    {
        StudentName = name;
        StudentId = studentId;
    }
    public static void SetNarrativeState(bool started, int stage, string label, string description,
        bool votingOpen, int continueVotes, int alternativeVotes, string advanceMode, string finalDecision, int voteRound)
    {
        int previousStage = CurrentNarrativeStage;
        if (started && stage == 3 && previousStage != 3)
            Handheld.Vibrate();
        NarrativeStarted = started;
        CurrentNarrativeStage = stage;
        CurrentNarrativeStageLabel = label;
        CurrentNarrativeStageDescription = description;
        VotingOpen = votingOpen;
        ContinueVotes = continueVotes;
        AlternativeVotes = alternativeVotes;
        AdvanceMode = advanceMode;
        FinalDecision = finalDecision;
        VoteRound = voteRound;
    }
    public static void SetNarrativeIntroCompleted(bool completed)
    {
        NarrativeIntroCompleted = completed;
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
    [SerializeField] private string sceneToLoad = "";

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
        string sessionId = StudentSessionContext.SessionId;
        string identityKey = studentName.Trim().ToLowerInvariant();
        string storageKey = "Serena.StudentId." + sessionId + "." + identityKey;
        string savedStudentId = PlayerPrefs.GetString(storageKey, string.Empty);
        if (string.IsNullOrWhiteSpace(savedStudentId))
        {
            savedStudentId = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(storageKey, savedStudentId);
            PlayerPrefs.Save();
        }
        var payload = new JoinStudentRequest { name = studentName, studentId = savedStudentId };
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

        PlayerPrefs.SetString(storageKey, response.studentId);
        PlayerPrefs.Save();
        ContinueToActivity(response.studentId);
        StudentHeartbeatRunner.Run(HeartbeatLoop(sessionId, response.studentId));
    }

    private IEnumerator HeartbeatLoop(string sessionId, string studentId)
    {
        string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
        while (StudentSessionContext.SessionId == sessionId && StudentSessionContext.StudentId == studentId)
        {
            yield return new WaitForSecondsRealtime(5f);
            using var request = new UnityWebRequest(baseUrl + "/api/sessions/" + sessionId + "/students/" + studentId + "/heartbeat", "POST");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.responseCode == 404) yield break;
        }
    }

    private void ContinueToActivity(string studentId)
    {
        StudentSessionContext.SetStudent(studentName, studentId);
        SaveStudentData(studentName, studentId, StudentSessionContext.SessionId);

        // Si se ha configurado una escena, cargarla. Si no, usar la navegación por paneles existente.
        if (!string.IsNullOrWhiteSpace(sceneToLoad))
        {
            SceneManager.LoadScene(sceneToLoad);
            return;
        }

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
    public string studentId;
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

public sealed class StudentHeartbeatRunner : MonoBehaviour
{
    private static StudentHeartbeatRunner instance;

    public static void Run(IEnumerator routine)
    {
        if (instance == null)
        {
            var host = new GameObject(nameof(StudentHeartbeatRunner));
            instance = host.AddComponent<StudentHeartbeatRunner>();
            DontDestroyOnLoad(host);
        }

        instance.StartCoroutine(routine);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
