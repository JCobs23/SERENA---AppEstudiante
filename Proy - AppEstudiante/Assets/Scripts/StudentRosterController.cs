using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

public sealed class StudentRosterController : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private TMP_Text connectedStudentsCount;
    [SerializeField] private Transform studentListContainer;
    [SerializeField] private StudentRowView studentRowPrefab;
    [SerializeField] private int maxStudentsToShow = 12;

    [Header("Indicadores RawImage")]
    [SerializeField] private Texture readyTexture;
    [SerializeField] private Texture notReadyTexture;
    [SerializeField] private Color readyColor = new(0.05f, 0.75f, 0.48f, 1f);
    [SerializeField] private Color notReadyColor = new(0.85f, 0.10f, 0.15f, 1f);

    [Header("Conexión")]
    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";
    [SerializeField] private float refreshSeconds = 1f;

    [Serializable]
    private sealed class RosterStudent
    {
        public string id;
        public string name;
        public bool ready;
        public bool connected;
    }

    [Serializable]
    private sealed class RosterResponse
    {
        public int count;
        public RosterStudent[] students;
    }

    private Coroutine refreshRoutine;

    private void OnEnable()
    {
        refreshRoutine = StartCoroutine(RefreshLoop());
    }

    private void OnDisable()
    {
        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);
    }

    private IEnumerator RefreshLoop()
    {
        while (true)
        {
            yield return RefreshRoster();
            yield return new WaitForSecondsRealtime(refreshSeconds);
        }
    }

    private IEnumerator RefreshRoster()
    {
        string sessionCode = StudentSessionContext.SessionId;
        if (string.IsNullOrWhiteSpace(sessionCode))
            yield break;

        string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
        string url = $"{baseUrl}/api/sessions/{sessionCode}/students/roster";

        using var request = UnityWebRequest.Get(url);
        request.timeout = 5;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"No se pudo consultar estudiantes: {request.error}");
            yield break;
        }

        RosterResponse response;
        try
        {
            response = JsonUtility.FromJson<RosterResponse>(request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Respuesta inválida: {exception.Message}");
            yield break;
        }

        ClearRows();

        int connectedCount = 0;
        int rowsCreated = 0;
        if (response?.students != null)
        {
            foreach (RosterStudent student in response.students)
            {
                if (student == null || !student.connected) continue;
                connectedCount++;

                if (rowsCreated >= Mathf.Max(0, maxStudentsToShow)) continue;
                StudentRowView row = Instantiate(studentRowPrefab, studentListContainer);
                row.Configure(student.name, student.ready, readyTexture, notReadyTexture, readyColor, notReadyColor);
                rowsCreated++;
            }
        }

        if (connectedStudentsCount != null)
            connectedStudentsCount.text = connectedCount.ToString();
    }

    private void ClearRows()
    {
        if (studentListContainer == null) return;
        for (int index = studentListContainer.childCount - 1; index >= 0; index--)
            Destroy(studentListContainer.GetChild(index).gameObject);
    }
}