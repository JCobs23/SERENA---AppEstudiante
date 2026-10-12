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
    }

    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";
    [SerializeField] private string narrativeSceneName = "3_Narrativa";
    [SerializeField] private float checkSeconds = 0.5f;

    private bool sceneLoaded;
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
                        StudentSessionContext.SetNarrativeState(
                            status.narrativeStarted,
                            status.stage,
                            status.stageLabel ?? string.Empty,
                            status.stageDescription ?? string.Empty
                        );

                        if (status.narrativeStarted && !sceneLoaded)
                        {
                            sceneLoaded = true;
                            SceneManager.LoadScene(narrativeSceneName);
                        }

                        if (status.narrativeStarted && !introStarted)
                        {
                            introStarted = true;
                            StartCoroutine(PlayIntroduction());
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
        introVisible = true;
        introError = null;
        string sourcePath = Path.Combine(Application.streamingAssetsPath, "SerenaIntro.mp4");
        byte[] videoBytes = null;

        using (var request = UnityWebRequest.Get(sourcePath))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();
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
            ShowIntroError("No se pudo preparar el video en este dispositivo.");
            yield break;
        }

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
        while (!videoPlayer.isPrepared && string.IsNullOrEmpty(introError) && waitSeconds < 30f)
        {
            waitSeconds += Time.unscaledDeltaTime;
            yield return null;
        }

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
        if (StudentSessionContext.NarrativeIntroCompleted)
            return;

        introVisible = false;
        StudentSessionContext.SetNarrativeIntroCompleted(true);
        StartCoroutine(ReportIntroductionCompleted());
    }

    private IEnumerator ReportIntroductionCompleted()
    {
        while (true)
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

        if (!StudentSessionContext.NarrativeIntroCompleted || StudentSessionContext.CurrentNarrativeStage == 0)
        {
            GUI.depth = -100;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
            GUI.color = Color.white;

            if (introVisible && videoTexture != null && videoPlayer != null && videoPlayer.isPlaying)
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), videoTexture, ScaleMode.ScaleToFit);

            EnsureStyles();
            if (StudentSessionContext.NarrativeIntroCompleted)
            {
                EnsureStyles();
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

        EnsureStyles();
        float width = Mathf.Min(Screen.width - 32, 1100);
        float x = (Screen.width - width) / 2f;
        Rect banner = new Rect(x, 20, width, 116);
        GUI.Box(banner, GUIContent.none, bannerStyle);
        GUI.Label(new Rect(x + 24, 32, width - 48, 34),
            "ETAPA " + StudentSessionContext.CurrentNarrativeStage + " · " + StudentSessionContext.CurrentNarrativeStageLabel,
            titleStyle);
        GUI.Label(new Rect(x + 24, 70, width - 48, 52),
            StudentSessionContext.CurrentNarrativeStageDescription,
            descriptionStyle);
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