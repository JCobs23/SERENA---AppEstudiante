using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

public class NarrativeStartListener : MonoBehaviour
{
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

    private bool introVisible;
    private string introError;
    private VideoPlayer videoPlayer;
    private RenderTexture videoTexture;
    private GUIStyle bannerStyle;
    private GUIStyle titleStyle;
    private GUIStyle descriptionStyle;
    private GUIStyle introTextStyle;
    private Texture2D bannerTexture;

    private void Start()
    {
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        StartCoroutine(SynchronizeNarrative());
        StartCoroutine(HeartbeatLoop());
    }

private IEnumerator SynchronizeNarrative()
    {
        while (true)
        {
            if (!string.IsNullOrWhiteSpace(StudentSessionContext.SessionId))
            {
                string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
                string url = baseUrl + "/api/sessions/" + StudentSessionContext.SessionId;
                using var request = UnityWebRequest.Get(url);
                request.timeout = 5;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    SessionStatus status = JsonUtility.FromJson<SessionStatus>(request.downloadHandler.text);
                    if (status != null)
                    {
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

                        NarrativeStageView stageView = FindAnyObjectByType<NarrativeStageView>();
                        if (stageView != null && status.narrativeStarted)
                        {
                            stageView.SetNarrativeState(
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
                }
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, checkSeconds));
        }
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

        videoTexture = new RenderTexture(Mathf.Max(640, Screen.width), Mathf.Max(360, Screen.height), 0, RenderTextureFormat.ARGB32);
        videoTexture.Create();
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
        if (!StudentSessionContext.NarrativeStarted)
            return;

        EnsureStyles();
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
            return;
        }

        // La vista de las etapas con contenido interactivo pertenece a 3_Narrativa.
        if (currentStage == 1 || currentStage == 2 || currentStage == 3)
            return;
        if (currentStage == 5)
            return;

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
    }

    private void OnDestroy()
    {
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
