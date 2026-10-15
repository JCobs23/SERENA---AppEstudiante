using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

public class NarrativeStageView : MonoBehaviour
{
    [SerializeField] private string videoFileName = "SerenaNarrativa.mp4";

    private int currentStage;
    private string stageLabel = string.Empty;
    private string stageDescription = string.Empty;
    private bool videoStarted;
    private bool videoFinished;
    private string videoError;
    private int generation;
    private int mediaRevision = -1;
    private int appliedMediaRevision = -1;
    private bool mediaPaused;
    private double mediaPositionSeconds;
    private string mediaCommand = string.Empty;
    private VideoPlayer videoPlayer;
    private RenderTexture videoTexture;
    private GUIStyle bannerStyle;
    private GUIStyle titleStyle;
    private GUIStyle descriptionStyle;
    private GUIStyle statusStyle;
    private Texture2D bannerTexture;

    public void SetNarrativeState(
        int stage,
        string label,
        string description,
        bool paused,
        double positionSeconds,
        int revision,
        string command
    )
    {
        currentStage = stage;
        stageLabel = label ?? string.Empty;
        stageDescription = description ?? string.Empty;
        mediaPaused = paused;
        mediaPositionSeconds = positionSeconds;
        mediaRevision = revision;
        mediaCommand = command ?? string.Empty;

        if (stage == 1 && !videoStarted)
        {
            videoStarted = true;
            StartCoroutine(PrepareAndPlayVideo());
        }
        else if (stage != 1 && videoStarted)
        {
            StopVideo();
        }

        ApplyMediaState();
    }

    private IEnumerator PrepareAndPlayVideo()
    {
        int currentGeneration = generation;
        videoError = null;
        string sourcePath = Path.Combine(Application.streamingAssetsPath, videoFileName);
        byte[] videoBytes = null;

        using (var request = UnityWebRequest.Get(sourcePath))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();

            if (currentGeneration != generation || currentStage != 1)
                yield break;

            if (request.result == UnityWebRequest.Result.Success)
                videoBytes = request.downloadHandler.data;
        }

        if (videoBytes == null || videoBytes.Length == 0)
        {
            videoError = "No se pudo cargar " + videoFileName + ". Revisa Assets/StreamingAssets.";
            Debug.LogWarning("Serena: no se pudo cargar el video de la Narrativa inicial.");
            yield break;
        }

        string localVideoPath = Path.Combine(Application.persistentDataPath, videoFileName);
        try
        {
            File.WriteAllBytes(localVideoPath, videoBytes);
        }
        catch (Exception)
        {
            if (currentGeneration == generation)
                videoError = "No se pudo preparar el video en este dispositivo.";
            yield break;
        }

        if (currentGeneration != generation || currentStage != 1)
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
        while (currentGeneration == generation
            && currentStage == 1
            && videoPlayer != null
            && !videoPlayer.isPrepared
            && string.IsNullOrEmpty(videoError)
            && waitSeconds < 30f)
        {
            waitSeconds += Time.unscaledDeltaTime;
            yield return null;
        }

        if (currentGeneration != generation || currentStage != 1)
            yield break;

        if (videoPlayer == null || !videoPlayer.isPrepared)
        {
            if (string.IsNullOrEmpty(videoError))
                videoError = "El video no pudo iniciar en este dispositivo.";
            yield break;
        }

        ApplyMediaState();
    }

    private void ApplyMediaState()
    {
        if (currentStage != 1 || videoPlayer == null || !videoPlayer.isPrepared)
            return;

        // Apply each teacher command against this tablet's actual playback clock.
        // Tablets can finish downloading/preparing at different times, so the
        // server's elapsed position is not a reliable seek target for live controls.
        if (appliedMediaRevision != mediaRevision)
        {
            switch (mediaCommand)
            {
                case "restart":
                    videoFinished = false;
                    videoPlayer.time = 0;
                    break;
                case "back-5":
                    videoPlayer.time = Math.Max(0, videoPlayer.time - 5);
                    break;
                case "forward-5":
                    double targetTime = videoPlayer.time + 5;
                    if (videoPlayer.length > 0)
                        targetTime = Math.Min(targetTime, Math.Max(0, videoPlayer.length - 0.1));
                    videoPlayer.time = targetTime;
                    break;
                case "sync":
                    videoPlayer.time = Math.Max(0, mediaPositionSeconds);
                    break;
            }

            appliedMediaRevision = mediaRevision;
        }

        // Reassert the requested play state on every API poll. This also recovers
        // when a VideoPlayer misses a pause/play transition during prepare or seek.
        if (mediaPaused)
        {
            if (videoPlayer.isPlaying)
                videoPlayer.Pause();
        }
        else if (!videoPlayer.isPlaying && !videoFinished)
        {
            videoPlayer.Play();
        }
    }

    private void HandleVideoEnded(VideoPlayer source)
    {
        videoFinished = true;
        source.Pause();
    }

    private void HandleVideoError(VideoPlayer source, string message)
    {
        videoError = "El video de la Narrativa inicial no es compatible con este dispositivo.";
        Debug.LogWarning("Serena: el video de la Narrativa inicial no es compatible con este dispositivo.");
    }

    private void StopVideo()
    {
        generation++;
        videoStarted = false;
        videoFinished = false;
        appliedMediaRevision = -1;
        videoError = null;

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

    private void OnGUI()
    {
        if (currentStage != 1)
            return;

        EnsureStyles();
        GUI.depth = -100;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
        GUI.color = Color.white;

        if (videoTexture != null && videoPlayer != null && videoPlayer.isPlaying)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), videoTexture, ScaleMode.ScaleToFit);

        float width = Mathf.Min(Screen.width - 40, 1200);
        float x = (Screen.width - width) / 2f;
        GUI.Box(new Rect(x, 20, width, 154), GUIContent.none, bannerStyle);
        GUI.Label(new Rect(x + 24, 34, width - 48, 48), "ETAPA 1 · " + stageLabel, titleStyle);
        GUI.Label(new Rect(x + 24, 84, width - 48, 78), stageDescription, descriptionStyle);

        if (!string.IsNullOrEmpty(videoError))
            GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 90), videoError, statusStyle);
        else if (videoPlayer == null || videoPlayer.isPlaying == false && videoPlayer.isPrepared == false)
            GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 60), "Preparando la Narrativa inicial…", statusStyle);
        else if (videoPlayer.isPrepared && !videoPlayer.isPlaying && videoPlayer.frame > 0)
            GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 60), "Narrativa finalizada. Esperando al docente…", statusStyle);
    }

    private void EnsureStyles()
    {
        if (bannerStyle != null)
            return;

        bannerTexture = new Texture2D(1, 1);
        bannerTexture.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.08f, 0.9f));
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

        statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.fontSize = Mathf.Clamp(Screen.width / 35, 20, 34);
        statusStyle.alignment = TextAnchor.MiddleCenter;
        statusStyle.wordWrap = true;
        statusStyle.normal.textColor = Color.white;
    }

    private void OnDestroy()
    {
        StopVideo();
        if (bannerTexture != null)
            Destroy(bannerTexture);
    }
}
