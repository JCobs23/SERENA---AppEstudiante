using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

public class NarrativeStartListener : MonoBehaviour
{
    private static NarrativeStartListener instance;

    private enum ConnectionState
    {
        Connecting,
        Connected,
        Lost,
        Reconnecting,
        Restored
    }

    [Serializable]
    private class SessionStatus
    {
        public bool narrativeStarted;
        public int stage;
        public string stageLabel;
        public string stageDescription;
        public bool mediaPaused;
        public double mediaPositionSeconds;
        public int mediaRevision;
        public string mediaCommand;
        public bool votingOpen;
        public int continueVotes;
        public int alternativeVotes;
        public string advanceMode;
        public string finalDecision;
        public int voteRound;
    }

    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";
    [SerializeField] private string codeEntrySceneName = "1_IngresoCodigo";
    [SerializeField] private string narrativeSceneName = "3_Narrativa";
    [SerializeField] private float checkSeconds = 0.5f;

    private bool sceneLoaded;
    private int narrativeGeneration;
    private bool introStarted;
    private bool duplicateInstance;
    private NarrativeStageView narrativeStageView;

    private bool introVisible;
    private string introError;
    private VideoPlayer videoPlayer;
    private RenderTexture videoTexture;
    private GUIStyle bannerStyle;
    private GUIStyle titleStyle;
    private GUIStyle descriptionStyle;
    private GUIStyle introTextStyle;
    private GUIStyle connectionTextStyle;
    private Texture2D bannerTexture;
    private ConnectionState connectionState = ConnectionState.Connecting;
    private string monitoredSessionId;
    private bool hasAttemptedSessionSync;
    private float connectionRestoredAt = -1f;

    private const float ConnectionRestoredDuration = 2.5f;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            duplicateInstance = true;
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void Start()
    {
        if (duplicateInstance)
            return;

        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        StartCoroutine(SynchronizeNarrative());
        StartCoroutine(HeartbeatLoop());
    }

    private void Update()
    {
        ResizeVideoTextureForCurrentScreen();
    }

    private void ResizeVideoTextureForCurrentScreen()
    {
        if (videoPlayer == null || videoTexture == null || !videoPlayer.isPlaying)
            return;

        Vector2Int size = GetFullscreenVideoTextureSize();
        if (videoTexture.width == size.x && videoTexture.height == size.y)
            return;

        RenderTexture resizedTexture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
        if (!resizedTexture.Create())
        {
            Destroy(resizedTexture);
            Debug.LogWarning("Serena: no se pudo reajustar la textura de la introducción para la pantalla actual.");
            return;
        }

        RenderTexture previousTexture = videoTexture;
        videoTexture = resizedTexture;
        videoPlayer.targetTexture = resizedTexture;
        previousTexture.Release();
        Destroy(previousTexture);
    }

    private static Vector2Int GetFullscreenVideoTextureSize()
    {
        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);
        float scale = Mathf.Max(1f, 640f / width, 360f / height);
        return new Vector2Int(Mathf.CeilToInt(width * scale), Mathf.CeilToInt(height * scale));
    }

private IEnumerator SynchronizeNarrative()
    {
        while (true)
        {
            if (!string.IsNullOrWhiteSpace(StudentSessionContext.SessionId))
            {
                string sessionId = StudentSessionContext.SessionId;
                if (monitoredSessionId != sessionId)
                    ResetConnectionState(sessionId);

                BeginSessionSyncAttempt();
                string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
                string url = baseUrl + "/api/sessions/" + sessionId;
                using var request = UnityWebRequest.Get(url);
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    SessionStatus status = null;
                    try
                    {
                        status = JsonUtility.FromJson<SessionStatus>(request.downloadHandler.text);
                    }
                    catch (Exception)
                    {
                        // Treat an unreadable status as a failed sync and keep polling.
                    }

                    if (status != null)
                    {
                        MarkSessionSyncSucceeded();
                        if (StudentSessionContext.NarrativeStarted && !status.narrativeStarted)
                        {
                            StopIntroduction();
                            sceneLoaded = false;
                            introStarted = false;
                            StudentSessionContext.ClearSession();
                            SceneManager.LoadScene(codeEntrySceneName);
                            continue;
                        }

                        if (status.narrativeStarted && status.stage >= 1
                            && !StudentSessionContext.NarrativeIntroCompleted)
                        {
                            StopIntroduction();
                            introStarted = true;
                            StudentSessionContext.SetNarrativeIntroCompleted(true);
                        }

                        StudentSessionContext.SetNarrativeState(
                            status.narrativeStarted,
                            status.stage,
                            status.stageLabel ?? string.Empty,
                            status.stageDescription ?? string.Empty,
                            status.votingOpen,
                            status.continueVotes,
                            status.alternativeVotes,
                            status.advanceMode ?? "automatic",
                            status.finalDecision ?? string.Empty,
                            status.voteRound
                        );

                        if (status.narrativeStarted && !sceneLoaded)
                        {
                            sceneLoaded = true;
                            SceneManager.LoadScene(narrativeSceneName);
                        }

                        if (status.narrativeStarted && status.stage == 0 && !introStarted)
                        {
                            introStarted = true;
                            StartCoroutine(PlayIntroduction());
                        }

                        if (narrativeStageView == null)
                            narrativeStageView = FindAnyObjectByType<NarrativeStageView>();
                        if (narrativeStageView != null && status.narrativeStarted)
                        {
                            narrativeStageView.SetNarrativeState(
                                status.stage,
                                status.stageLabel ?? string.Empty,
                                status.stageDescription ?? string.Empty,
                                status.mediaPaused,
                                status.mediaPositionSeconds,
                                status.mediaRevision,
                                status.mediaCommand ?? string.Empty,
                                status.votingOpen,
                                status.continueVotes,
                                status.alternativeVotes,
                                status.advanceMode ?? "automatic",
                                status.finalDecision ?? string.Empty,
                                status.voteRound
                            );
                        }
                    }
                    else
                    {
                        MarkSessionSyncFailed();
                    }
                }
                else
                {
                    MarkSessionSyncFailed();
                }
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, checkSeconds));
        }
    }

    private void ResetConnectionState(string sessionId)
    {
        monitoredSessionId = sessionId;
        connectionState = ConnectionState.Connecting;
        hasAttemptedSessionSync = false;
        connectionRestoredAt = -1f;
    }

    private void BeginSessionSyncAttempt()
    {
        if (connectionState == ConnectionState.Lost
            || (connectionState == ConnectionState.Connecting && hasAttemptedSessionSync))
        {
            connectionState = ConnectionState.Reconnecting;
        }

        hasAttemptedSessionSync = true;
    }

    private void MarkSessionSyncSucceeded()
    {
        if (connectionState == ConnectionState.Lost || connectionState == ConnectionState.Reconnecting)
        {
            connectionState = ConnectionState.Restored;
            connectionRestoredAt = Time.unscaledTime;
        }
        else if (connectionState != ConnectionState.Restored)
        {
            connectionState = ConnectionState.Connected;
        }
    }

    private void MarkSessionSyncFailed()
    {
        connectionState = ConnectionState.Lost;
        connectionRestoredAt = -1f;
    }

    private IEnumerator HeartbeatLoop()
    {
        while (true)
        {
            if (!string.IsNullOrWhiteSpace(StudentSessionContext.SessionId)
                && !string.IsNullOrWhiteSpace(StudentSessionContext.StudentId))
            {
                string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
                string url = baseUrl + "/api/sessions/" + StudentSessionContext.SessionId
                    + "/students/" + StudentSessionContext.StudentId + "/heartbeat";
                using var request = new UnityWebRequest(url, "POST");
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = 5;
                yield return request.SendWebRequest();
            }

            yield return new WaitForSecondsRealtime(3f);
        }
    }
private IEnumerator PlayIntroduction()
    {
        int generation = narrativeGeneration;
        introVisible = true;
        introError = null;
        string sourcePath = Path.Combine(Application.streamingAssetsPath, "SerenaIntro.mp4");
        byte[] videoBytes = null;

        using (var request = UnityWebRequest.Get(sourcePath))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();
            if (generation != narrativeGeneration || !StudentSessionContext.NarrativeStarted)
                yield break;
            if (request.result == UnityWebRequest.Result.Success)
                videoBytes = request.downloadHandler.data;
        }

        if (videoBytes == null || videoBytes.Length == 0)
        {
            ShowIntroError("No se pudo cargar la introducción. Revisa que SerenaIntro.mp4 esté en Assets/StreamingAssets.");
            yield break;
        }

        string localVideoPath = Path.Combine(Application.persistentDataPath, "SerenaIntro.mp4");
        try
        {
            File.WriteAllBytes(localVideoPath, videoBytes);
        }
        catch (Exception)
        {
            if (generation == narrativeGeneration)
                ShowIntroError("No se pudo preparar el video en este dispositivo.");
            yield break;
        }

        if (generation != narrativeGeneration || !StudentSessionContext.NarrativeStarted)
            yield break;

        Vector2Int introTextureSize = GetFullscreenVideoTextureSize();
        RenderTexture introTexture = new RenderTexture(
            introTextureSize.x, introTextureSize.y, 0, RenderTextureFormat.ARGB32);
        if (!introTexture.Create())
        {
            Destroy(introTexture);
            ShowIntroError("No se pudo preparar el video en este dispositivo.");
            yield break;
        }
        videoTexture = introTexture;
        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = localVideoPath;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoTexture;
        videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetDirectAudioMute(0, false);
        videoPlayer.SetDirectAudioVolume(0, 1f);
        videoPlayer.isLooping = false;
        videoPlayer.skipOnDrop = true;
        videoPlayer.errorReceived += HandleVideoError;
        videoPlayer.loopPointReached += HandleVideoEnded;
        videoPlayer.Prepare();

        float waitSeconds = 0f;
        while (generation == narrativeGeneration
            && StudentSessionContext.NarrativeStarted
            && !videoPlayer.isPrepared
            && string.IsNullOrEmpty(introError)
            && waitSeconds < 30f)
        {
            waitSeconds += Time.unscaledDeltaTime;
            yield return null;
        }

        if (generation != narrativeGeneration || !StudentSessionContext.NarrativeStarted)
            yield break;

        if (!videoPlayer.isPrepared)
        {
            ShowIntroError(string.IsNullOrEmpty(introError) ? "El video no pudo iniciar en este dispositivo." : introError);
            yield break;
        }

        videoPlayer.Play();
    }

    private void HandleVideoEnded(VideoPlayer source)
    {
        FinishIntroduction();
    }

    private void HandleVideoError(VideoPlayer source, string message)
    {
        ShowIntroError("El video no es compatible con este dispositivo.");
    }

    private void ShowIntroError(string message)
    {
        introError = message;
        introVisible = true;
        Debug.LogWarning("Serena: no se pudo reproducir la introducción.");
    }

    private void FinishIntroduction()
    {
        if (!StudentSessionContext.NarrativeStarted || StudentSessionContext.NarrativeIntroCompleted)
            return;

        introVisible = false;
        StudentSessionContext.SetNarrativeIntroCompleted(true);
        StartCoroutine(ReportIntroductionCompleted());
    }

    private void StopIntroduction()
    {
        StopVideoPlayback();
        StudentSessionContext.SetNarrativeIntroCompleted(false);
    }

    private void StopVideoPlayback()
    {
        narrativeGeneration++;
        introVisible = false;
        introError = null;
        if (videoPlayer != null)
        {
            videoPlayer.errorReceived -= HandleVideoError;
            videoPlayer.loopPointReached -= HandleVideoEnded;
            videoPlayer.Stop();
            Destroy(videoPlayer);
            videoPlayer = null;
        }
        if (videoTexture != null)
        {
            videoTexture.Release();
            Destroy(videoTexture);
            videoTexture = null;
        }
    }

private IEnumerator ReportIntroductionCompleted()
    {
        while (StudentSessionContext.NarrativeStarted
            && !string.IsNullOrWhiteSpace(StudentSessionContext.SessionId)
            && !string.IsNullOrWhiteSpace(StudentSessionContext.StudentId))
        {
            string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
            string url = baseUrl + "/api/sessions/" + StudentSessionContext.SessionId
                + "/students/" + StudentSessionContext.StudentId + "/intro-complete";
            using var request = new UnityWebRequest(url, "POST");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 8;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
                yield break;
            yield return new WaitForSecondsRealtime(2f);
        }
    }

    private void OnGUI()
    {
        if (string.IsNullOrWhiteSpace(StudentSessionContext.SessionId))
            return;

        EnsureStyles();
        if (!StudentSessionContext.NarrativeStarted)
        {
            DrawConnectionIndicator();
            return;
        }

        int currentStage = StudentSessionContext.CurrentNarrativeStage;

        if (!StudentSessionContext.NarrativeIntroCompleted || currentStage == 0)
        {
            GUI.depth = -100;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
            GUI.color = Color.white;

            if (introVisible && videoTexture != null && videoPlayer != null && videoPlayer.isPlaying)
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), videoTexture, ScaleMode.ScaleToFit);

            if (StudentSessionContext.NarrativeIntroCompleted)
            {
                GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 60), "Introducción terminada. Esperando al grupo…", introTextStyle);
                DrawConnectionIndicator();
                return;
            }
            if (!string.IsNullOrEmpty(introError))
            {
                GUI.Box(new Rect(24, Screen.height * 0.34f, Screen.width - 48, Screen.height * 0.32f), GUIContent.none, bannerStyle);
                GUI.Label(new Rect(48, Screen.height * 0.38f, Screen.width - 96, Screen.height * 0.16f), introError, introTextStyle);
                if (GUI.Button(new Rect(Screen.width * 0.25f, Screen.height * 0.58f, Screen.width * 0.5f, 58), "Continuar sin introducción"))
                    FinishIntroduction();
            }
            else if (videoPlayer == null || !videoPlayer.isPlaying)
            {
                GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 60), "Preparando introducción…", introTextStyle);
            }
            DrawConnectionIndicator();
            return;
        }

        // La vista de las etapas con contenido interactivo pertenece a 3_Narrativa.
        if (currentStage == 1 || currentStage == 2 || currentStage == 3 || currentStage == 4)
        {
            // NarrativeStageView owns the video and controls for these stages.
            DrawConnectionIndicator();
            return;
        }
        if (currentStage == 5)
        {
            DrawConnectionIndicator();
            return;
        }

        GUI.depth = -100;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
        GUI.color = Color.white;

        float stageWidth = Mathf.Min(Screen.width - 32, 1100);
        float stageX = (Screen.width - stageWidth) / 2f;
        GUI.Box(new Rect(stageX, 20, stageWidth, 154), GUIContent.none, bannerStyle);
        GUI.Label(new Rect(stageX + 24, 34, stageWidth - 48, 48),
            "ETAPA " + currentStage + " · " + StudentSessionContext.CurrentNarrativeStageLabel, titleStyle);
        GUI.Label(new Rect(stageX + 24, 84, stageWidth - 48, 78),
            StudentSessionContext.CurrentNarrativeStageDescription, descriptionStyle);
        if (currentStage == 5 && StudentSessionContext.FinalDecision == "continue")
            GUI.Label(new Rect(stageX + 24, 170, stageWidth - 48, 54),
                "El docente eligió continuar a la siguiente narrativa.", descriptionStyle);
        DrawConnectionIndicator();
    }

    private void DrawConnectionIndicator()
    {
        if ((videoPlayer != null && videoPlayer.isPlaying)
            || ((StudentSessionContext.CurrentNarrativeStage == 1
                || StudentSessionContext.CurrentNarrativeStage == 4)
                && narrativeStageView == null)
            || (narrativeStageView != null && narrativeStageView.IsFullscreenVideoPlaying))
        {
            return;
        }

        if (connectionState == ConnectionState.Restored
            && Time.unscaledTime - connectionRestoredAt >= ConnectionRestoredDuration)
        {
            connectionState = ConnectionState.Connected;
        }

        string label;
        Color background;
        switch (connectionState)
        {
            case ConnectionState.Connected:
                label = "Conectado";
                background = new Color(0.08f, 0.48f, 0.30f, 0.94f);
                break;
            case ConnectionState.Lost:
                label = "Conexión perdida · servidor no disponible";
                background = new Color(0.62f, 0.12f, 0.12f, 0.96f);
                break;
            case ConnectionState.Reconnecting:
                label = "Reconectando…";
                background = new Color(0.62f, 0.39f, 0.08f, 0.96f);
                break;
            case ConnectionState.Restored:
                label = "Conexión restablecida";
                background = new Color(0.08f, 0.48f, 0.30f, 0.96f);
                break;
            default:
                label = "Conectando…";
                background = new Color(0.30f, 0.34f, 0.40f, 0.94f);
                break;
        }

        Rect safeArea = Screen.safeArea;
        float width = Mathf.Max(1f, Mathf.Min(340f, safeArea.width - 20f));
        float height = 34f;
        float x = safeArea.xMax - width - 10f;
        float y = Screen.height - safeArea.yMax + 8f;
        Rect indicatorRect = new Rect(x, y, width, height);

        int previousDepth = GUI.depth;
        Color previousColor = GUI.color;
        GUI.depth = -200;
        GUI.color = background;
        GUI.DrawTexture(indicatorRect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(indicatorRect, label, connectionTextStyle);
        GUI.color = previousColor;
        GUI.depth = previousDepth;
    }

    private void EnsureStyles()
    {
        if (bannerStyle != null)
            return;

        bannerTexture = new Texture2D(1, 1);
        bannerTexture.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.08f, 0.88f));
        bannerTexture.Apply();

        bannerStyle = new GUIStyle(GUI.skin.box);
        bannerStyle.normal.background = bannerTexture;
        bannerStyle.padding = new RectOffset(8, 8, 8, 8);

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = Mathf.Clamp(Screen.width / 42, 20, 36);
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.wordWrap = true;
        titleStyle.normal.textColor = Color.white;

        descriptionStyle = new GUIStyle(GUI.skin.label);
        descriptionStyle.fontSize = Mathf.Clamp(Screen.width / 65, 16, 26);
        descriptionStyle.wordWrap = true;
        descriptionStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);

        introTextStyle = new GUIStyle(GUI.skin.label);
        introTextStyle.fontSize = Mathf.Clamp(Screen.width / 35, 20, 34);
        introTextStyle.alignment = TextAnchor.MiddleCenter;
        introTextStyle.wordWrap = true;
        introTextStyle.normal.textColor = Color.white;

        connectionTextStyle = new GUIStyle(GUI.skin.label);
        connectionTextStyle.fontSize = Mathf.Clamp(Screen.width / 90, 12, 17);
        connectionTextStyle.fontStyle = FontStyle.Bold;
        connectionTextStyle.alignment = TextAnchor.MiddleCenter;
        connectionTextStyle.wordWrap = false;
        connectionTextStyle.normal.textColor = Color.white;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;

        if (videoPlayer != null)
        {
            videoPlayer.errorReceived -= HandleVideoError;
            videoPlayer.loopPointReached -= HandleVideoEnded;
            videoPlayer.Stop();
        }
        if (videoTexture != null)
        {
            videoTexture.Release();
            Destroy(videoTexture);
        }
        if (bannerTexture != null)
            Destroy(bannerTexture);
    }
}
