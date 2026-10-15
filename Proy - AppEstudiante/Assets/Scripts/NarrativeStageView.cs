using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

public class NarrativeStageView : MonoBehaviour
{
    [SerializeField] private string videoFileName = "SerenaNarrativa.mp4";
    [SerializeField] private string serverBaseUrl = "http://192.168.1.18:5080";

    private static readonly string[] EmotionValues =
        { "Curioso", "Miedo a la exclusión", "Neutral", "Confundido", "Incómodo" };
    private static readonly string[] DecisionValues =
        { "Dialogar directamente", "Evitar involucrarse", "Difundir el rumor", "Cambiar el tema" };

    [Serializable]
    private class InteractionRequest
    {
        public string value;
    }

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
    private bool pauseBlurCapturePending;
    private double mediaPositionSeconds;
    private string mediaCommand = string.Empty;
    private VideoPlayer videoPlayer;
    private RenderTexture videoTexture;
    private Texture2D blurredEndFrame;
    private GUIStyle bannerStyle;
    private GUIStyle titleStyle;
    private GUIStyle descriptionStyle;
    private GUIStyle statusStyle;
    private GUIStyle timelineStyle;
    private GUIStyle emotionHeaderStyle;
    private GUIStyle emotionSubheaderStyle;
    private GUIStyle emotionIconStyle;
    private GUIStyle emotionLabelStyle;
    private GUIStyle emotionSelectedStyle;
    private GUIStyle emotionCardStyle;
    private GUIStyle emotionSelectedCardStyle;
    private GUIStyle decisionCardStyle;
    private GUIStyle decisionSelectedCardStyle;
    private Texture2D bannerTexture;
    private Texture2D emotionCardTexture;
    private Texture2D emotionSelectedCardTexture;
    private readonly Texture2D[] emotionImages = new Texture2D[5];
    private int selectedEmotionIndex = -1;
    private int selectedDecisionIndex = -1;
    private bool isSubmittingInteraction;
    private string interactionMessage;

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
        int previousStage = currentStage;
        currentStage = stage;
        stageLabel = label ?? string.Empty;
        stageDescription = description ?? string.Empty;
        mediaPaused = paused;
        mediaPositionSeconds = positionSeconds;
        mediaRevision = revision;
        mediaCommand = command ?? string.Empty;

        if (stage == 2 && previousStage != 2)
        {
            selectedEmotionIndex = -1;
            interactionMessage = string.Empty;
        }
        if (stage == 3 && previousStage != 3)
        {
            selectedDecisionIndex = -1;
            interactionMessage = string.Empty;
        }

        if (previousStage == 2 && stage != 2 && blurredEndFrame != null)
        {
            Destroy(blurredEndFrame);
            blurredEndFrame = null;
        }

        if (stage == 1 && !videoStarted)
        {
            videoStarted = true;
            StartCoroutine(PrepareAndPlayVideo());
        }
        else if (stage != 1 && videoStarted)
        {
            if (stage == 2 && blurredEndFrame == null && videoTexture != null)
                blurredEndFrame = CreateBlurredSnapshot();
            StopVideo(stage == 2);
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

            if (!videoFinished && blurredEndFrame == null && !pauseBlurCapturePending)
            {
                pauseBlurCapturePending = true;
                StartCoroutine(CaptureBlurredFrame(false));
            }
        }
        else if (!videoPlayer.isPlaying && !videoFinished)
        {
            videoPlayer.Play();
        }

        if (!mediaPaused && !videoFinished && blurredEndFrame != null)
        {
            Destroy(blurredEndFrame);
            blurredEndFrame = null;
        }
    }

    private void HandleVideoEnded(VideoPlayer source)
    {
        videoFinished = true;
        source.Pause();
        StartCoroutine(CaptureBlurredFrame(true));
    }

    private IEnumerator CaptureBlurredFrame(bool captureForEnd)
    {
        // Let VideoPlayer settle after a pause/seek before sampling its render texture.
        yield return null;
        yield return new WaitForEndOfFrame();
        pauseBlurCapturePending = false;
        if (videoTexture == null || (captureForEnd ? !videoFinished : !mediaPaused || videoFinished))
            yield break;

        Texture2D snapshot = CreateBlurredSnapshot();
        if (snapshot != null)
        {
            if (blurredEndFrame != null)
                Destroy(blurredEndFrame);
            blurredEndFrame = snapshot;
        }
    }

    private Texture2D CreateBlurredSnapshot()
    {
        if (videoTexture == null)
            return null;

        int width = Mathf.Clamp(Screen.width / 8, 96, 320);
        int height = Mathf.Max(54, Mathf.RoundToInt(width * Screen.height / (float)Mathf.Max(1, Screen.width)));
        RenderTexture sampleTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = RenderTexture.active;
        Texture2D sample = null;
        Texture2D result = null;

        try
        {
            Graphics.Blit(videoTexture, sampleTexture);
            RenderTexture.active = sampleTexture;
            sample = new Texture2D(width, height, TextureFormat.RGB24, false);
            sample.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            sample.Apply(false, false);
            BlurSmallTexture(sample, 3);
            result = sample;
            sample = null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Serena: no se pudo preparar el fondo borroso de la narrativa. " + exception.Message);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            RenderTexture.ReleaseTemporary(sampleTexture);
            if (sample != null)
                Destroy(sample);
        }

        return result;
    }

    private static void BlurSmallTexture(Texture2D texture, int radius)
    {
        Color32[] source = texture.GetPixels32();
        Color32[] blurred = new Color32[source.Length];
        int width = texture.width;
        int height = texture.height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int red = 0;
                int green = 0;
                int blue = 0;
                int samples = 0;
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    int sampleY = y + offsetY;
                    if (sampleY < 0 || sampleY >= height)
                        continue;
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        int sampleX = x + offsetX;
                        if (sampleX < 0 || sampleX >= width)
                            continue;
                        Color32 color = source[sampleY * width + sampleX];
                        red += color.r;
                        green += color.g;
                        blue += color.b;
                        samples++;
                    }
                }
                blurred[y * width + x] = new Color32(
                    (byte)(red / samples), (byte)(green / samples), (byte)(blue / samples), 255);
            }
        }

        texture.SetPixels32(blurred);
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply(false, false);
    }

    private static string FormatTime(double seconds)
    {
        int totalSeconds = Math.Max(0, (int)Math.Floor(seconds));
        return (totalSeconds / 60).ToString("00") + ":" + (totalSeconds % 60).ToString("00");
    }

    private void HandleVideoError(VideoPlayer source, string message)
    {
        videoError = "El video de la Narrativa inicial no es compatible con este dispositivo.";
        Debug.LogWarning("Serena: el video de la Narrativa inicial no es compatible con este dispositivo.");
    }

    private void StopVideo(bool preserveBlurredFrame = false)
    {
        generation++;
        videoStarted = false;
        videoFinished = false;
        pauseBlurCapturePending = false;
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

        if (!preserveBlurredFrame && blurredEndFrame != null)
        {
            Destroy(blurredEndFrame);
            blurredEndFrame = null;
        }
    }

    private void OnGUI()
    {
        if (currentStage == 2 || currentStage == 3)
        {
            EnsureStyles();
            if (currentStage == 2)
                DrawEmotionSelection();
            else
                DrawDecisionSelection();
            return;
        }

        if (currentStage != 1)
            return;

        EnsureStyles();
        GUI.depth = -100;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
        GUI.color = Color.white;

        if ((videoFinished || mediaPaused) && blurredEndFrame != null)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), blurredEndFrame, ScaleMode.ScaleToFit);
        else if (videoTexture != null && videoPlayer != null && (videoPlayer.isPlaying || mediaPaused))
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
        else if (videoPlayer.isPrepared && videoFinished)
            GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 60), "Narrativa finalizada. Esperando al docente…", statusStyle);

        DrawTimeline();
    }

    private void DrawEmotionSelection()
    {
        GUI.depth = -100;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
        GUI.color = Color.white;
        if (blurredEndFrame != null)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), blurredEndFrame, ScaleMode.ScaleToFit);
        GUI.color = new Color(0, 0, 0, 0.28f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float headerY = Screen.height * 0.035f;
        GUI.Label(new Rect(16, headerY, Screen.width - 32, Screen.height * 0.065f),
            "¿Qué emoción estás sintiendo ahora?", emotionHeaderStyle);
        GUI.Label(new Rect(24, headerY + Screen.height * 0.065f, Screen.width - 48, Screen.height * 0.05f),
            "Toca la opción que más se parece a lo que sientes", emotionSubheaderStyle);

        string[] emotions =
        {
            "🤔\nSiento curiosidad",
            "😟\nMe preocupa\nquedar fuera",
            "😐\nNo siento nada\nrelevante",
            "😵\nEstoy confundido",
            "😰\nMe siento incómodo"
        };
        string[] imageNames = { "curiosidad", "fuera", "neutro", "confundido", "incomodo" };
        float gapX = Screen.width * 0.035f;
        float gapY = Screen.height * 0.018f;
        float cardWidth = Mathf.Min((Screen.width - gapX - 36f) / 2f, 520f);
        float cardHeight = Mathf.Min(Screen.height * 0.215f, 430f);
        float gridWidth = cardWidth * 2 + gapX;
        float left = (Screen.width - gridWidth) / 2f;
        float top = Screen.height * 0.17f;

        for (int i = 0; i < emotions.Length; i++)
        {
            int row = i / 2;
            bool centered = i == 4;
            float x = centered ? (Screen.width - cardWidth) / 2f : left + (i % 2) * (cardWidth + gapX);
            float y = top + row * (cardHeight + gapY);
            Rect card = new Rect(x, y, cardWidth, cardHeight);
            bool selected = selectedEmotionIndex == i;
            GUI.enabled = !isSubmittingInteraction;
            if (GUI.Button(card, GUIContent.none, selected ? emotionSelectedCardStyle : emotionCardStyle))
            {
                selectedEmotionIndex = i;
                StartCoroutine(SubmitInteraction("emotion", EmotionValues[i]));
            }
            GUI.enabled = true;

            if (emotionImages[i] != null)
            {
                GUI.DrawTexture(card, emotionImages[i], ScaleMode.StretchToFill, true);
                if (selected)
                    DrawSelectionOutline(card);
                continue;
            }

            string[] lines = emotions[i].Split('\n');
            GUI.Label(new Rect(x + 10, y + cardHeight * 0.08f, cardWidth - 20, cardHeight * 0.48f), lines[0], emotionIconStyle);
            string label = string.Join("\n", lines, 1, lines.Length - 1);
            GUI.Label(new Rect(x + 18, y + cardHeight * 0.56f, cardWidth - 36, cardHeight * 0.31f), label, emotionLabelStyle);
            if (selected)
                GUI.Label(new Rect(x + 12, y + cardHeight * 0.86f, cardWidth - 24, cardHeight * 0.1f), "Seleccionada", emotionSelectedStyle);
        }

        DrawInteractionStatus();
    }

    private void DrawDecisionSelection()
    {
        GUI.depth = -100;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
        GUI.color = Color.white;
        if (blurredEndFrame != null)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), blurredEndFrame, ScaleMode.ScaleToFit);
        GUI.color = new Color(0, 0, 0, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float headerY = Screen.height * 0.08f;
        GUI.Label(new Rect(20, headerY, Screen.width - 40, Screen.height * 0.09f),
            "¿Qué decisión tomarías?", emotionHeaderStyle);
        GUI.Label(new Rect(24, headerY + Screen.height * 0.09f, Screen.width - 48, Screen.height * 0.07f),
            "Elige cómo responder a esta situación", emotionSubheaderStyle);

        float gapX = Screen.width * 0.035f;
        float gapY = Screen.height * 0.025f;
        float cardWidth = Mathf.Min((Screen.width - gapX - 36f) / 2f, 560f);
        float cardHeight = Mathf.Min(Screen.height * 0.22f, 260f);
        float left = (Screen.width - (cardWidth * 2 + gapX)) / 2f;
        float top = Screen.height * 0.32f;

        for (int i = 0; i < DecisionValues.Length; i++)
        {
            float x = left + (i % 2) * (cardWidth + gapX);
            float y = top + (i / 2) * (cardHeight + gapY);
            Rect card = new Rect(x, y, cardWidth, cardHeight);
            GUI.enabled = !isSubmittingInteraction;
            if (GUI.Button(card, DecisionValues[i],
                    selectedDecisionIndex == i ? decisionSelectedCardStyle : decisionCardStyle))
            {
                selectedDecisionIndex = i;
                StartCoroutine(SubmitInteraction("decision", DecisionValues[i]));
            }
            GUI.enabled = true;
        }

        DrawInteractionStatus();
    }

    private void DrawInteractionStatus()
    {
        if (string.IsNullOrEmpty(interactionMessage))
            return;

        GUI.Label(new Rect(24, Screen.height * 0.91f, Screen.width - 48, Screen.height * 0.06f),
            interactionMessage, statusStyle);
    }

    private IEnumerator SubmitInteraction(string kind, string value)
    {
        if (isSubmittingInteraction || string.IsNullOrWhiteSpace(StudentSessionContext.SessionId)
            || string.IsNullOrWhiteSpace(StudentSessionContext.StudentId))
        {
            interactionMessage = "No se pudo identificar tu sesión. Vuelve a conectarte.";
            yield break;
        }

        isSubmittingInteraction = true;
        interactionMessage = "Enviando respuesta…";
        string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
        string url = baseUrl + "/api/sessions/" + StudentSessionContext.SessionId
            + "/students/" + StudentSessionContext.StudentId + "/" + kind;
        string json = JsonUtility.ToJson(new InteractionRequest { value = value });
        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 8;
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
            interactionMessage = "Respuesta enviada correctamente.";
        else
        {
            interactionMessage = "No se pudo enviar. Comprueba la conexión e inténtalo de nuevo.";
            Debug.LogWarning("Serena: fallo al enviar la respuesta (" + request.responseCode + "): " + request.error);
        }
        isSubmittingInteraction = false;
    }

    private static void DrawSelectionOutline(Rect card)
    {
        const float thickness = 7f;
        GUI.color = new Color(0.12f, 0.78f, 0.49f, 1f);
        GUI.DrawTexture(new Rect(card.x, card.y, card.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(card.x, card.yMax - thickness, card.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(card.x, card.y, thickness, card.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(card.xMax - thickness, card.y, thickness, card.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void DrawTimeline()
    {
        if (videoPlayer == null || !videoPlayer.isPrepared || videoPlayer.length <= 0)
            return;

        double duration = videoPlayer.length;
        double currentTime = Math.Min(duration, Math.Max(0, videoPlayer.time));
        float progress = (float)(currentTime / duration);
        float margin = Mathf.Clamp(Screen.width * 0.055f, 20, 64);
        float barWidth = Screen.width - margin * 2;
        float barHeight = Mathf.Clamp(Screen.height * 0.009f, 8, 14);
        float barY = Screen.height - Mathf.Clamp(Screen.height * 0.055f, 28, 58);

        GUI.color = new Color(0, 0, 0, 0.72f);
        GUI.DrawTexture(new Rect(margin - 10, barY - 31, barWidth + 20, barHeight + 42), Texture2D.whiteTexture);
        GUI.color = new Color(1, 1, 1, 0.28f);
        GUI.DrawTexture(new Rect(margin, barY, barWidth, barHeight), Texture2D.whiteTexture);
        GUI.color = new Color(0.94f, 0.12f, 0.14f, 1);
        GUI.DrawTexture(new Rect(margin, barY, barWidth * progress, barHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(margin, barY - 27, barWidth, 24),
            FormatTime(currentTime) + " / " + FormatTime(duration), timelineStyle);
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

        timelineStyle = new GUIStyle(GUI.skin.label);
        timelineStyle.fontSize = Mathf.Clamp(Screen.width / 75, 16, 24);
        timelineStyle.alignment = TextAnchor.MiddleCenter;
        timelineStyle.normal.textColor = Color.white;

        emotionHeaderStyle = new GUIStyle(GUI.skin.label);
        emotionHeaderStyle.fontSize = Mathf.Clamp(Screen.width / 24, 28, 52);
        emotionHeaderStyle.fontStyle = FontStyle.Bold;
        emotionHeaderStyle.alignment = TextAnchor.MiddleCenter;
        emotionHeaderStyle.wordWrap = true;
        emotionHeaderStyle.normal.textColor = Color.white;

        emotionSubheaderStyle = new GUIStyle(GUI.skin.label);
        emotionSubheaderStyle.fontSize = Mathf.Clamp(Screen.width / 45, 18, 30);
        emotionSubheaderStyle.alignment = TextAnchor.MiddleCenter;
        emotionSubheaderStyle.wordWrap = true;
        emotionSubheaderStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);

        emotionIconStyle = new GUIStyle(GUI.skin.label);
        emotionIconStyle.fontSize = Mathf.Clamp((int)(Screen.width * 0.095f), 48, 108);
        emotionIconStyle.alignment = TextAnchor.MiddleCenter;
        emotionIconStyle.normal.textColor = new Color(0.22f, 0.16f, 0.25f);

        emotionLabelStyle = new GUIStyle(GUI.skin.label);
        emotionLabelStyle.fontSize = Mathf.Clamp(Screen.width / 32, 22, 38);
        emotionLabelStyle.fontStyle = FontStyle.Bold;
        emotionLabelStyle.alignment = TextAnchor.MiddleCenter;
        emotionLabelStyle.wordWrap = true;
        emotionLabelStyle.normal.textColor = new Color(0.16f, 0.11f, 0.2f);

        emotionSelectedStyle = new GUIStyle(GUI.skin.label);
        emotionSelectedStyle.fontSize = Mathf.Clamp(Screen.width / 60, 16, 24);
        emotionSelectedStyle.fontStyle = FontStyle.Bold;
        emotionSelectedStyle.alignment = TextAnchor.MiddleCenter;
        emotionSelectedStyle.normal.textColor = new Color(0.1f, 0.48f, 0.31f);

        if (emotionCardTexture == null)
            emotionCardTexture = CreateRoundedCardTexture(new Color(0.96f, 0.93f, 0.89f, 0.98f), new Color(0.72f, 0.67f, 0.6f, 1f));
        if (emotionSelectedCardTexture == null)
            emotionSelectedCardTexture = CreateRoundedCardTexture(new Color(1f, 0.98f, 0.93f, 1f), new Color(0.2f, 0.7f, 0.48f, 1f));

        string[] emotionImageNames = { "curiosidad", "fuera", "neutro", "confundido", "incomodo" };
        for (int i = 0; i < emotionImages.Length; i++)
        {
            if (emotionImages[i] == null)
                emotionImages[i] = Resources.Load<Texture2D>("Emociones/" + emotionImageNames[i]);
        }

        emotionCardStyle = new GUIStyle(GUI.skin.button);
        emotionCardStyle.normal.background = emotionCardTexture;
        emotionCardStyle.hover.background = emotionCardTexture;
        emotionCardStyle.active.background = emotionCardTexture;
        emotionCardStyle.border = new RectOffset(18, 18, 18, 18);
        emotionCardStyle.padding = new RectOffset(0, 0, 0, 0);
        emotionCardStyle.margin = new RectOffset(0, 0, 0, 0);
        emotionSelectedCardStyle = new GUIStyle(emotionCardStyle);
        emotionSelectedCardStyle.normal.background = emotionSelectedCardTexture;
        emotionSelectedCardStyle.hover.background = emotionSelectedCardTexture;
        emotionSelectedCardStyle.active.background = emotionSelectedCardTexture;

        decisionCardStyle = new GUIStyle(emotionCardStyle);
        decisionCardStyle.fontSize = Mathf.Clamp(Screen.width / 34, 22, 38);
        decisionCardStyle.fontStyle = FontStyle.Bold;
        decisionCardStyle.alignment = TextAnchor.MiddleCenter;
        decisionCardStyle.wordWrap = true;
        decisionCardStyle.normal.textColor = new Color(0.16f, 0.11f, 0.2f);
        decisionCardStyle.hover.textColor = decisionCardStyle.normal.textColor;
        decisionCardStyle.active.textColor = decisionCardStyle.normal.textColor;

        decisionSelectedCardStyle = new GUIStyle(decisionCardStyle);
        decisionSelectedCardStyle.normal.background = emotionSelectedCardTexture;
        decisionSelectedCardStyle.hover.background = emotionSelectedCardTexture;
        decisionSelectedCardStyle.active.background = emotionSelectedCardTexture;
    }

    private static Texture2D CreateRoundedCardTexture(Color fill, Color border)
    {
        const int size = 64;
        const float radius = 12f;
        const float borderWidth = 3f;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs(x + 0.5f - size * 0.5f) - (size * 0.5f - radius);
                float py = Mathf.Abs(y + 0.5f - size * 0.5f) - (size * 0.5f - radius);
                float distance = Mathf.Min(Mathf.Max(px, py), 0f)
                    + new Vector2(Mathf.Max(px, 0f), Mathf.Max(py, 0f)).magnitude - radius;
                Color pixel = distance > 0f ? new Color(0, 0, 0, 0)
                    : distance > -borderWidth ? border : fill;
                texture.SetPixel(x, y, pixel);
            }
        }
        texture.Apply(false, true);
        return texture;
    }

    private void OnDestroy()
    {
        StopVideo();
        if (bannerTexture != null)
            Destroy(bannerTexture);
        if (emotionCardTexture != null)
            Destroy(emotionCardTexture);
        if (emotionSelectedCardTexture != null)
            Destroy(emotionSelectedCardTexture);
    }
}
