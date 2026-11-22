using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Video;

public class NarrativeStageView : MonoBehaviour
{
    [SerializeField] private string videoFileName = "SerenaNarrativa.mp4";

    private static readonly string[] EmotionValues =
        { "Curious", "Fear of exclusion", "Neutral", "Confused", "Uncomfortable" };
    private static readonly string[] DecisionValues =
        { "Talk directly", "Avoid getting involved", "Spread the rumor", "Change the subject" };
    // Etapa 4: cada tablet reproduce la consecuencia de la decisión que eligió ese estudiante.
    // Mismo orden que DecisionValues.
    private static readonly string[] DecisionVideoFiles =
    {
        "Decision_DialogarDirectamente.mp4",
        "Decision_EvitarInvolucrarse.mp4",
        "Decision_DifundirRumor.mp4",
        "Decision_CambiarTema.mp4"
    };
    // H.U. 7.1.3: mensaje breve y neutral que aparece al terminar la consecuencia.
    // Mismo orden que DecisionValues.
    private static readonly string[] ConsequenceShortMessages =
    {
        "Talking directly opened a space to clear up what happened and listen to the people involved.",
        "By not stepping in, the situation went on without a conversation and each person read it their own way.",
        "The rumor reached more people, and those involved may have felt exposed.",
        "Changing the subject stopped the conversation, but what happened was never cleared up."
    };

    [Serializable]
    private class InteractionRequest
    {
        public string value;
    }

    [Serializable]
    private class VoteRequest
    {
        public string choice;
    }
    [Serializable]
    private class ReflectionRequest
    {
        public string answers;
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
    private int volumePercent = 100;
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
    private GUIStyle emotionPressedCardStyle;
    private GUIStyle emotionSelectedCardStyle;
    private GUIStyle decisionCardStyle;
    private GUIStyle decisionSelectedCardStyle;
    private Texture2D bannerTexture;
    private Texture2D emotionCardTexture;
    private Texture2D emotionSelectedCardTexture;
    private Texture2D emotionDisabledCardTexture;
    private Texture2D emotionDisabledSelectedCardTexture;
    private Texture2D decisionPressedCardTexture;
    private GUIStyle disabledCardStyle;
    private GUIStyle blockedDecisionTextStyle;
    private GUIStyle blockedDecisionTagStyle;
    private GUIStyle consequenceTitleStyle;
    private GUIStyle consequenceTextStyle;
    private readonly bool[] blockedDecisions = new bool[4];
    private readonly Texture2D[] emotionImages = new Texture2D[5];
    private int selectedEmotionIndex = -1;
    private int pressedEmotionIndex = -1;
    private int selectedDecisionIndex = -1;
    private bool isSubmittingInteraction;
    private string interactionMessage;
    private bool votingOpen;
    private bool voteSubmitted;
    private bool isSubmittingVote;
    private bool voteLockedByConflict;
    private string submittedVoteChoice = string.Empty;
    private int voteRound = -1;
    private int continueVotes;
    private int alternativeVotes;
    private string advanceMode = "automatic";
    private string finalDecision;
    private string voteMessage;
    private readonly bool?[] reflectionAnswers = new bool?[4];
    private string reflectionMessage = string.Empty;
    private bool reflectionSubmitted;
    private bool isSubmittingReflection;
    private GUIStyle reflectionButtonStyle;
    private GUIStyle reflectionSelectedButtonStyle;
    private GUIStyle reflectionTextStyle;
    private GUIStyle reflectionSmallStyle;
    private float transitionStartedAt = -10f;
    // H.U. 10.x (Edwin): línea de tiempo, menú de progreso y mensajes positivos por etapa.
    private StudentProgressView progressView;
    private AudioSource confirmationAudio;
    private AudioClip confirmationClip;

    // Lo usa NarrativeStartListener para ocultar el indicador de conexión sobre el video.
    public bool IsFullscreenVideoPlaying => videoPlayer != null && videoPlayer.isPlaying;

    // H.U. 9.3.1: el servidor envía las decisiones más votadas en rondas anteriores.
    public void SetBlockedDecisions(string[] values)
    {
        for (int i = 0; i < blockedDecisions.Length; i++)
            blockedDecisions[i] = values != null && Array.IndexOf(values, DecisionValues[i]) >= 0;

        if (selectedDecisionIndex >= 0 && IsDecisionBlocked(selectedDecisionIndex) && currentStage == 3)
            selectedDecisionIndex = -1;
    }

    private bool IsDecisionBlocked(int index) =>
        index >= 0 && index < blockedDecisions.Length && blockedDecisions[index];

    private bool HasBlockedDecisions() => Array.IndexOf(blockedDecisions, true) >= 0;

    private void Update()
    {
        ResizeVideoTextureForCurrentScreen();
    }

    // H.U. 5.1.2 (Bryan): el área del video se ajusta si la tablet cambia de orientación.
    private void ResizeVideoTextureForCurrentScreen()
    {
        if (videoPlayer == null || videoTexture == null || !videoPlayer.isPlaying)
            return;

        Vector2Int size = GetVideoTextureSize(currentStage);
        if (videoTexture.width == size.x && videoTexture.height == size.y)
            return;

        RenderTexture resizedTexture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
        if (!resizedTexture.Create())
        {
            Destroy(resizedTexture);
            Debug.LogWarning("Serena: no se pudo reajustar el área de video para la pantalla actual.");
            return;
        }

        RenderTexture previousTexture = videoTexture;
        videoTexture = resizedTexture;
        videoPlayer.targetTexture = resizedTexture;
        previousTexture.Release();
        Destroy(previousTexture);
    }

    private Rect GetVideoViewport(int stage)
    {
        if (stage != 4)
            return new Rect(0f, 0f, Mathf.Max(1f, Screen.width), Mathf.Max(1f, Screen.height));

        // Etapa 4: deja libre el encabezado y la línea de tiempo del video.
        const float topReserved = 156f;
        float bottomReserved = Mathf.Clamp(Screen.height * 0.055f, 28f, 58f) + 39f;
        return new Rect(0f, topReserved, Mathf.Max(1f, Screen.width),
            Mathf.Max(1f, Screen.height - topReserved - bottomReserved));
    }

    private Vector2Int GetVideoTextureSize(int stage)
    {
        Rect viewport = GetVideoViewport(stage);
        float width = Mathf.Max(1f, viewport.width);
        float height = Mathf.Max(1f, viewport.height);
        float scale = Mathf.Max(1f, 640f / width, 360f / height);
        return new Vector2Int(Mathf.CeilToInt(width * scale), Mathf.CeilToInt(height * scale));
    }

    private bool CreateVideoTexture(int stage)
    {
        Vector2Int size = GetVideoTextureSize(stage);
        RenderTexture texture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32);
        if (!texture.Create())
        {
            Destroy(texture);
            return false;
        }

        videoTexture = texture;
        return true;
    }

    public void SetNarrativeState(
        int stage,
        string label,
        string description,
        bool paused,
        double positionSeconds,
        int revision,
        string command,
        bool hasOpenVote,
        int continueVoteCount,
        int alternativeVoteCount,
        string mode,
        string decision,
        int currentVoteRound,
        int volume = 100
    )
    {
        volumePercent = Mathf.Clamp(volume, 0, 100);
        int previousStage = currentStage;
        if (previousStage != stage && previousStage > 0 && stage > 1)
            transitionStartedAt = Time.unscaledTime;

        if (progressView == null)
        {
            progressView = GetComponent<StudentProgressView>();
            if (progressView == null)
                progressView = gameObject.AddComponent<StudentProgressView>();
            progressView.Initialize(this);
        }
        progressView.UpdateStage(stage, label,
            exploringAlternative: stage == 3 && previousStage > 3 && HasBlockedDecisions());
        currentStage = stage;
        stageLabel = label ?? string.Empty;
        stageDescription = description ?? string.Empty;
        mediaPaused = paused;
        mediaPositionSeconds = positionSeconds;
        mediaRevision = revision;
        mediaCommand = command ?? string.Empty;
        votingOpen = hasOpenVote;
        continueVotes = continueVoteCount;
        alternativeVotes = alternativeVoteCount;
        advanceMode = mode ?? "automatic";
        finalDecision = decision ?? string.Empty;
        if (voteRound != currentVoteRound)
        {
            voteRound = currentVoteRound;
            voteSubmitted = false;
            voteLockedByConflict = false;
            submittedVoteChoice = string.Empty;
            voteMessage = string.Empty;
        }

        if (stage == 2 && previousStage != 2)
        {
            selectedEmotionIndex = -1;
            interactionMessage = string.Empty;
        }
        if (stage == 5 && previousStage != 5)
        {
            Array.Clear(reflectionAnswers, 0, reflectionAnswers.Length);
            reflectionMessage = string.Empty;
            reflectionSubmitted = false;
            isSubmittingReflection = false;
        }
        if (stage == 3 && previousStage != 3)
        {
            selectedDecisionIndex = -1;
            interactionMessage = string.Empty;
        }

        if (stage == 0 && blurredEndFrame != null)
        {
            Destroy(blurredEndFrame);
            blurredEndFrame = null;
        }

        if (stage == 1 && !videoStarted)
        {
            videoStarted = true;
            StartCoroutine(PrepareBlurredPreview());
        }
        else if (stage == 4 && previousStage != 4)
        {
            if (videoStarted)
                StopVideo(true);
            if (selectedDecisionIndex >= 0 && selectedDecisionIndex < DecisionVideoFiles.Length)
            {
                videoStarted = true;
                StartCoroutine(PrepareDecisionVideo(DecisionVideoFiles[selectedDecisionIndex]));
            }
            else
            {
                videoError = "No decision was recorded on this tablet, so there is no video to show.";
                Debug.LogWarning("Serena: la etapa 4 comenzó sin una decisión seleccionada en este dispositivo.");
            }
        }
        else if (stage != 1 && stage != 4 && videoStarted)
        {
            if (stage == 2 && blurredEndFrame == null && videoTexture != null)
                blurredEndFrame = CreateBlurredSnapshot();
            // Al salir de la etapa 4 se conserva el último fotograma borroso de la consecuencia.
            StopVideo(stage == 2 || previousStage == 4);
        }

        ApplyMediaState();
    }

    private IEnumerator PrepareBlurredPreview()
    {
        int currentGeneration = generation;
        yield return StartCoroutine(PrepareVideoPlayer(videoFileName, 1, false));
        if (currentGeneration != generation || currentStage != 1 || videoPlayer == null || !videoPlayer.isPrepared)
            yield break;

        // El video completo se reproduce en la proyección web del docente.
        // En la tablet solo se decodifica un fotograma para mostrarlo borroso.
        videoPlayer.Play();
        yield return new WaitForSecondsRealtime(0.2f);
        if (currentGeneration != generation || videoPlayer == null || currentStage != 1)
            yield break;

        videoPlayer.Pause();
        yield return new WaitForEndOfFrame();
        blurredEndFrame = CreateBlurredSnapshot();
        videoPlayer.Stop();
    }

    private IEnumerator PrepareDecisionVideo(string decisionVideoFile)
    {
        int currentGeneration = generation;
        yield return StartCoroutine(PrepareVideoPlayer(decisionVideoFile, 4, true));
        if (currentGeneration != generation || currentStage != 4 || videoPlayer == null || !videoPlayer.isPrepared)
            yield break;

        // El docente controla pausa, ±5 s y volumen desde la proyección.
        ApplyMediaState();
    }

    // Copia el video de StreamingAssets al almacenamiento local (necesario en Android)
    // y deja un VideoPlayer preparado. Quien lo llama decide si reproducirlo.
    private IEnumerator PrepareVideoPlayer(string fileName, int expectedStage, bool withAudio)
    {
        int currentGeneration = generation;
        videoError = null;
        string sourcePath = Path.Combine(Application.streamingAssetsPath, fileName);
        byte[] videoBytes = null;

        using (var request = UnityWebRequest.Get(sourcePath))
        {
            request.timeout = 30;
            yield return request.SendWebRequest();

            if (currentGeneration != generation || currentStage != expectedStage)
                yield break;

            if (request.result == UnityWebRequest.Result.Success)
                videoBytes = request.downloadHandler.data;
        }

        if (videoBytes == null || videoBytes.Length == 0)
        {
            videoError = "Could not load " + fileName + ". Check Assets/StreamingAssets.";
            Debug.LogWarning("Serena: no se pudo cargar el video " + fileName + ".");
            yield break;
        }

        string localVideoPath = Path.Combine(Application.persistentDataPath, fileName);
        try
        {
            File.WriteAllBytes(localVideoPath, videoBytes);
        }
        catch (Exception)
        {
            if (currentGeneration == generation)
                videoError = "The video could not be prepared on this device.";
            yield break;
        }

        if (currentGeneration != generation || currentStage != expectedStage)
            yield break;

        if (!CreateVideoTexture(expectedStage))
        {
            videoError = "The video area could not be prepared on this device.";
            yield break;
        }

        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = localVideoPath;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoTexture;
        videoPlayer.aspectRatio = VideoAspectRatio.FitInside;
        if (withAudio)
        {
            videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            videoPlayer.EnableAudioTrack(0, true);
            videoPlayer.SetDirectAudioMute(0, false);
            videoPlayer.SetDirectAudioVolume(0, volumePercent / 100f);
        }
        else
        {
            videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        }
        videoPlayer.isLooping = false;
        videoPlayer.skipOnDrop = true;
        videoPlayer.errorReceived += HandleVideoError;
        videoPlayer.loopPointReached += HandleVideoEnded;
        videoPlayer.Prepare();

        float waitSeconds = 0f;
        while (currentGeneration == generation
            && currentStage == expectedStage
            && videoPlayer != null
            && !videoPlayer.isPrepared
            && string.IsNullOrEmpty(videoError)
            && waitSeconds < 30f)
        {
            waitSeconds += Time.unscaledDeltaTime;
            yield return null;
        }

        if (currentGeneration != generation || currentStage != expectedStage)
            yield break;

        if (videoPlayer == null || !videoPlayer.isPrepared)
        {
            if (string.IsNullOrEmpty(videoError))
                videoError = "The video could not start on this device.";
        }
    }

    // Aplica en la etapa 4 los controles del docente (pausa, ±5 s, volumen)
    // sobre el reloj de reproducción real de esta tablet.
    private void ApplyMediaState()
    {
        if (currentStage != 4 || videoPlayer == null || !videoPlayer.isPrepared)
            return;

        if (appliedMediaRevision != mediaRevision)
        {
            switch (mediaCommand)
            {
                case "restart":
                    videoFinished = false;
                    videoPlayer.time = 0;
                    break;
                case "back-5":
                    videoFinished = false;
                    videoPlayer.time = Math.Max(0, videoPlayer.time - 5);
                    break;
                case "forward-5":
                    double targetTime = videoPlayer.time + 5;
                    if (videoPlayer.length > 0)
                        targetTime = Math.Min(targetTime, Math.Max(0, videoPlayer.length - 0.1));
                    videoPlayer.time = targetTime;
                    break;
            }

            appliedMediaRevision = mediaRevision;
        }

        videoPlayer.SetDirectAudioVolume(0, volumePercent / 100f);

        // Se reafirma el estado en cada consulta a la API por si el VideoPlayer
        // perdió una transición de pausa/reproducción mientras se preparaba.
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
        int height = Mathf.Max(54, Mathf.RoundToInt(width * videoTexture.height / (float)Mathf.Max(1, videoTexture.width)));
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
        videoError = "The video is not supported on this device.";
        Debug.LogWarning("Serena: el video no es compatible con este dispositivo. " + message);
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
        EnsureStyles();
        if (currentStage == 5)
        {
            if (votingOpen) DrawVotingPanel();
            else DrawReflectionPanel();
            DrawTransitionFeedback();
            return;
        }

        if (currentStage == 2 || currentStage == 3)
        {
            if (currentStage == 2)
                DrawEmotionSelection();
            else
                DrawDecisionSelection();
            DrawTransitionFeedback();
            return;
        }

        if (currentStage == 4)
        {
            DrawDecisionConsequenceVideo();
            DrawTransitionFeedback();
            return;
        }

        // Etapa 1: la narrativa se proyecta en la pantalla del docente;
        // la tablet solo muestra el fotograma borroso.
        if (currentStage != 1)
            return;

        GUI.depth = -100;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
        GUI.color = Color.white;

        if (blurredEndFrame != null)
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), blurredEndFrame, ScaleMode.ScaleToFit);

        float width = Mathf.Min(Screen.width - 40, 1200);
        float x = (Screen.width - width) / 2f;
        GUI.Box(new Rect(x, 20, width, 154), GUIContent.none, bannerStyle);
        GUI.Label(new Rect(x + 24, 34, width - 48, 48), "STAGE 1 · " + stageLabel, titleStyle);
        GUI.Label(new Rect(x + 24, 84, width - 48, 78), stageDescription, descriptionStyle);

        if (!string.IsNullOrEmpty(videoError))
            GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 90), videoError, statusStyle);
        else
            GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 60), "The story is playing on the classroom screen.", statusStyle);
        DrawTransitionFeedback();
    }

    // Etapa 4: video de la consecuencia de la decisión que eligió este estudiante.
    private void DrawDecisionConsequenceVideo()
    {
        GUI.depth = -100;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.blackTexture);
        GUI.color = Color.white;

        bool videoReady = videoPlayer != null && videoPlayer.isPrepared && videoTexture != null;
        Rect videoViewport = GetVideoViewport(4);
        if (videoReady && !videoFinished)
            GUI.DrawTexture(videoViewport, videoTexture, ScaleMode.ScaleToFit);
        else if (blurredEndFrame != null)
            GUI.DrawTexture(videoViewport, blurredEndFrame, ScaleMode.ScaleToFit);

        float width = Mathf.Min(Screen.width - 40, 1200);
        float x = (Screen.width - width) / 2f;
        GUI.Box(new Rect(x, 20, width, 126), GUIContent.none, bannerStyle);
        GUI.Label(new Rect(x + 24, 34, width - 48, 42), "STAGE 4 · " + stageLabel, titleStyle);
        string decisionText = selectedDecisionIndex >= 0 && selectedDecisionIndex < DecisionValues.Length
            ? "Your decision: " + DecisionValues[selectedDecisionIndex]
            : stageDescription;
        GUI.Label(new Rect(x + 24, 78, width - 48, 54), decisionText, descriptionStyle);

        bool hasDecision = selectedDecisionIndex >= 0 && selectedDecisionIndex < DecisionValues.Length;
        if (!string.IsNullOrEmpty(videoError))
        {
            // Sin video, el mensaje de la consecuencia se muestra igual para no perder el impacto.
            if (hasDecision)
                DrawConsequenceMessage(videoError);
            else
                GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 90), videoError, statusStyle);
        }
        else if (!videoReady)
            GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 60), "Preparing the video for your decision…", statusStyle);
        else if (videoFinished)
            DrawConsequenceMessage("Wait for the teacher's signal to continue.");
        else if (mediaPaused)
            GUI.Label(new Rect(24, Screen.height * 0.45f, Screen.width - 48, 60), "The teacher paused the video.", statusStyle);

        DrawTimeline();
    }

    // H.U. 7.1.3: mensaje breve, neutral y legible tras la consecuencia. Ocupa el centro de la
    // pantalla (la etapa 4 no tiene botones) y desaparece cuando el docente cambia de etapa.
    private void DrawConsequenceMessage(string footer)
    {
        if (selectedDecisionIndex < 0 || selectedDecisionIndex >= ConsequenceShortMessages.Length)
            return;

        float width = Mathf.Min(Screen.width - 48f, 860f);
        float x = (Screen.width - width) / 2f;
        float padding = Mathf.Clamp(width * 0.045f, 18f, 36f);
        float contentWidth = width - padding * 2f;
        const string title = "What may have happened";
        string message = ConsequenceShortMessages[selectedDecisionIndex];
        const string prompt = "Think about how the people involved may have felt.";
        float titleHeight = consequenceTitleStyle.CalcHeight(new GUIContent(title), contentWidth);
        float messageHeight = consequenceTextStyle.CalcHeight(new GUIContent(message), contentWidth);
        float promptHeight = descriptionStyle.CalcHeight(new GUIContent(prompt), contentWidth);
        float footerHeight = string.IsNullOrEmpty(footer) ? 0f : descriptionStyle.CalcHeight(new GUIContent(footer), contentWidth);
        float height = padding * 2f + titleHeight + messageHeight + promptHeight + footerHeight + 36f;
        Rect viewport = GetVideoViewport(4);
        float y = Mathf.Max(viewport.y + 8f, viewport.center.y - height / 2f);

        GUI.color = new Color(1f, 1f, 1f, 0.97f);
        GUI.Box(new Rect(x, y, width, height), GUIContent.none, bannerStyle);
        GUI.color = Color.white;
        float cursorY = y + padding;
        GUI.Label(new Rect(x + padding, cursorY, contentWidth, titleHeight), title, consequenceTitleStyle);
        cursorY += titleHeight + 12f;
        GUI.Label(new Rect(x + padding, cursorY, contentWidth, messageHeight), message, consequenceTextStyle);
        cursorY += messageHeight + 12f;
        GUI.Label(new Rect(x + padding, cursorY, contentWidth, promptHeight), prompt, descriptionStyle);
        cursorY += promptHeight + 12f;
        if (footerHeight > 0f)
            GUI.Label(new Rect(x + padding, cursorY, contentWidth, footerHeight), footer, descriptionStyle);
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

        float sidebarWidth = Mathf.Clamp(Screen.width * 0.23f, 280f, 390f);
        float contentWidth = Screen.width - sidebarWidth - 48f;
        float headerY = Screen.height * 0.035f;
        GUI.Label(new Rect(16, headerY, contentWidth, Screen.height * 0.065f),
            "What emotion are you feeling right now?", emotionHeaderStyle);
        GUI.Label(new Rect(24, headerY + Screen.height * 0.065f, contentWidth - 16f, Screen.height * 0.05f),
            "Tap the option closest to what you feel", emotionSubheaderStyle);

        string[] emotions =
        {
            "🤔\nI feel curious",
            "😟\nI'm worried about\nbeing left out",
            "😐\nI don't feel\nanything special",
            "😵\nI feel confused",
            "😰\nI feel uncomfortable"
        };
        string[] imageNames = { "curiosidad", "fuera", "neutro", "confundido", "incomodo" };
        // H.U. 5.x (Bryan): la cuadrícula pasa a una columna en pantallas estrechas.
        float gapX = Mathf.Clamp(contentWidth * 0.035f, 8f, 24f);
        float gapY = Mathf.Clamp(Screen.height * 0.018f, 8f, 20f);
        float twoColumnCardWidth = Mathf.Min((contentWidth - gapX - 36f) / 2f, 390f);
        int columns = twoColumnCardWidth < 160f ? 1 : 2;
        int rows = Mathf.CeilToInt(emotions.Length / (float)columns);
        float cardWidth = columns == 1 ? contentWidth : twoColumnCardWidth;
        float top = Screen.height * 0.17f;
        float gridBottom = Screen.height * 0.88f;
        float availableCardHeight = (gridBottom - top - gapY * (rows - 1)) / rows;
        float cardHeight = Mathf.Max(48f, Mathf.Min(Screen.height * 0.215f, 430f, availableCardHeight));
        float gridWidth = cardWidth * columns + gapX * (columns - 1);
        float left = (contentWidth - gridWidth) / 2f;

        for (int i = 0; i < emotions.Length; i++)
        {
            int row = i / columns;
            int column = i % columns;
            bool centered = columns == 2 && i == emotions.Length - 1 && emotions.Length % columns != 0;
            float x = centered ? (contentWidth - cardWidth) / 2f : left + column * (cardWidth + gapX);
            float y = top + row * (cardHeight + gapY);
            Rect card = new Rect(x, y, cardWidth, cardHeight);
            bool selected = selectedEmotionIndex == i;
            if (Event.current.type == EventType.MouseDown && card.Contains(Event.current.mousePosition))
                pressedEmotionIndex = i;
            bool pressing = pressedEmotionIndex == i;
            GUI.enabled = !isSubmittingInteraction;
            GUIStyle cardStyle = pressing ? emotionPressedCardStyle : selected ? emotionSelectedCardStyle : emotionCardStyle;
            if (GUI.Button(card, GUIContent.none, cardStyle))
            {
                selectedEmotionIndex = i;
                StartCoroutine(SubmitInteraction("emotion", EmotionValues[i]));
            }
            GUI.enabled = true;

            if (emotionImages[i] != null)
            {
                GUI.DrawTexture(card, emotionImages[i], ScaleMode.StretchToFill, true);
                if (isSubmittingInteraction)
                {
                    GUI.color = new Color(0.62f, 0.62f, 0.62f, 0.45f);
                    GUI.DrawTexture(card, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                else if (pressing)
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.14f);
                    GUI.DrawTexture(card, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                if (selected)
                    DrawSelectionOutline(card);
                if (Event.current.type == EventType.MouseUp && pressing)
                    pressedEmotionIndex = -1;
                continue;
            }

            string[] lines = emotions[i].Split('\n');
            GUI.Label(new Rect(x + 10, y + cardHeight * 0.08f, cardWidth - 20, cardHeight * 0.48f), lines[0], emotionIconStyle);
            string label = string.Join("\n", lines, 1, lines.Length - 1);
            GUI.Label(new Rect(x + 18, y + cardHeight * 0.56f, cardWidth - 36, cardHeight * 0.31f), label, emotionLabelStyle);
            if (selected)
                GUI.Label(new Rect(x + 12, y + cardHeight * 0.86f, cardWidth - 24, cardHeight * 0.1f), "Selected", emotionSelectedStyle);
            if (Event.current.type == EventType.MouseUp && pressing)
                pressedEmotionIndex = -1;
        }

        DrawNarrativeSidebar("Time to recognize the emotion", 2);
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
        GUI.color = new Color(0, 0, 0, 0.28f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float sidebarWidth = Mathf.Clamp(Screen.width * 0.23f, 280f, 390f);
        float contentWidth = Screen.width - sidebarWidth - 48f;
        float headerY = Screen.height * 0.045f;
        GUI.Label(new Rect(16, headerY, contentWidth, Screen.height * 0.09f), "What decision would you make?", emotionHeaderStyle);
        bool hasBlocked = HasBlockedDecisions();
        GUI.Label(new Rect(24, headerY + Screen.height * 0.09f, contentWidth - 16, Screen.height * 0.07f),
            hasBlocked
                ? "The blurred options were the most voted before. Choose an alternative."
                : "Choose how to respond. You can change your selection before confirming it.", emotionSubheaderStyle);

        // H.U. 5.x (Bryan): cuadrícula adaptable. H.U. 9.3.1: opciones más votadas desenfocadas.
        float gapX = Mathf.Clamp(contentWidth * 0.035f, 8f, 24f);
        float gapY = Mathf.Clamp(Screen.height * 0.018f, 8f, 20f);
        float twoColumnCardWidth = Mathf.Min((contentWidth - gapX - 36f) / 2f, 420f);
        int columns = twoColumnCardWidth < 160f ? 1 : 2;
        int rows = Mathf.CeilToInt(DecisionValues.Length / (float)columns);
        float cardWidth = columns == 1 ? contentWidth : twoColumnCardWidth;
        float left = (contentWidth - (cardWidth * columns + gapX * (columns - 1))) / 2f;
        float top = Screen.height * 0.28f;
        float confirmY = Screen.height * 0.82f;
        float availableCardHeight = (confirmY - top - gapY * rows) / rows;
        float cardHeight = Mathf.Max(48f, Mathf.Min(Screen.height * 0.22f, 260f, availableCardHeight));
        for (int i = 0; i < DecisionValues.Length; i++)
        {
            int row = i / columns;
            int column = i % columns;
            float x = left + column * (cardWidth + gapX);
            float y = top + row * (cardHeight + gapY);
            Rect card = new Rect(x, y, cardWidth, cardHeight);
            if (IsDecisionBlocked(i))
            {
                DrawBlockedDecisionCard(card, DecisionValues[i]);
                continue;
            }
            GUI.enabled = !isSubmittingInteraction;
            if (GUI.Button(card, DecisionValues[i], selectedDecisionIndex == i ? decisionSelectedCardStyle : decisionCardStyle))
                selectedDecisionIndex = i;
            GUI.enabled = true;
        }
        Rect confirm = new Rect((contentWidth - Mathf.Min(contentWidth * 0.56f, 500f)) / 2f,
            confirmY, Mathf.Min(contentWidth * 0.56f, 500f), Mathf.Clamp(Screen.height * 0.075f, 58f, 84f));
        bool canConfirm = selectedDecisionIndex >= 0 && !IsDecisionBlocked(selectedDecisionIndex);
        GUI.enabled = canConfirm && !isSubmittingInteraction;
        GUIStyle confirmStyle = canConfirm ? decisionSelectedCardStyle : disabledCardStyle;
        if (GUI.Button(confirm, isSubmittingInteraction ? "SENDING…" : "CONFIRM DECISION", confirmStyle))
            StartCoroutine(SubmitInteraction("decision", DecisionValues[selectedDecisionIndex]));
        GUI.enabled = true;
        DrawNarrativeSidebar("Time to make a decision", 3);
        DrawInteractionStatus();
    }

    // H.U. 9.3.1: tarjeta deshabilitada con el texto desenfocado (varias copias desplazadas
    // y translúcidas) para que la opción ya elegida por la mayoría no se pueda volver a marcar.
    private void DrawBlockedDecisionCard(Rect card, string text)
    {
        GUI.enabled = false;
        GUI.Box(card, GUIContent.none, disabledCardStyle);
        GUI.enabled = true;

        Rect textArea = new Rect(card.x + 12f, card.y + 6f, card.width - 24f, card.height * 0.7f);
        const float spread = 2.5f;
        Color previous = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.16f);
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            GUI.Label(new Rect(textArea.x + dx * spread, textArea.y + dy * spread, textArea.width, textArea.height),
                text, blockedDecisionTextStyle);

        GUI.color = new Color(1f, 1f, 1f, 0.35f);
        GUI.DrawTexture(card, Texture2D.whiteTexture);
        GUI.color = previous;
        GUI.Label(new Rect(card.x + 8f, card.yMax - Mathf.Min(44f, card.height * 0.32f), card.width - 16f,
            Mathf.Min(36f, card.height * 0.28f)), "Most voted before · not available", blockedDecisionTagStyle);
    }

    private void DrawNarrativeSidebar(string heading, int activeStep)
    {
        float width = Mathf.Clamp(Screen.width * 0.23f, 280f, 390f);
        float x = Screen.width - width - 18f;
        float y = Screen.height * 0.12f;
        float height = Screen.height * 0.72f;
        GUI.Box(new Rect(x, y, width, height), GUIContent.none, bannerStyle);
        GUI.Label(new Rect(x + 18, y + 14, width - 36, 48), heading, emotionSubheaderStyle);
        GUI.Label(new Rect(x + 18, y + 78, width - 36, 30), "KEY POINT", emotionSelectedStyle);
        GUI.Label(new Rect(x + 18, y + 110, width - 36, 100),
            "Think about how the people involved feel. Your answer is individual and there is no right option.", descriptionStyle);
        GUI.Label(new Rect(x + 18, y + 226, width - 36, 30), "REMEMBER", emotionSelectedStyle);
        GUI.Label(new Rect(x + 18, y + 258, width - 36, 78),
            "Answer respectfully and choose what you really think.", descriptionStyle);
        GUI.Label(new Rect(x + 18, y + height - 72, width - 36, 32), "Stage " + activeStep + " of 5", emotionSelectedStyle);
        Rect progress = new Rect(x + 18, y + height - 32, width - 36, 8);
        GUI.color = new Color(1, 1, 1, 0.2f);
        GUI.DrawTexture(progress, Texture2D.whiteTexture);
        GUI.color = new Color(0.84f, 0.11f, 0.15f, 1);
        GUI.DrawTexture(new Rect(progress.x, progress.y, progress.width * activeStep / 5f, progress.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // H.U. 7.1.4 y 7.3.1: preguntas de reflexión relacionadas con la decisión tomada.
    // Las respuestas se envían sin nombre (solo sí/no) y no bloquean el avance del docente.
    private void DrawReflectionPanel()
    {
        if (reflectionSubmitted)
        {
            DrawReflectionWaitScreen();
            return;
        }

        GUI.depth = -100;
        GUI.color = new Color(0.97f, 0.97f, 0.97f, 1f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(24, 18, Screen.width - 48, 48), "Serena  ·  Reflection time", reflectionTextStyle);
        GUI.Label(new Rect(40, Screen.height * 0.075f, Screen.width - 80, 64), "Let's reflect on what happened", reflectionTextStyle);
        GUI.Label(new Rect(40, Screen.height * 0.145f, Screen.width - 80, 42),
            GetReflectionContextPrompt(), reflectionSmallStyle);

        float width = Mathf.Min(Screen.width * 0.88f, 980f);
        float x = (Screen.width - width) / 2f;
        float y = Screen.height * 0.22f;
        float height = Screen.height * 0.56f;
        GUI.Box(new Rect(x, y, width, height), GUIContent.none, emotionCardStyle);
        GUI.Label(new Rect(x + 24, y + 16, width - 48, 76),
            GetNeutralConsequenceText(), reflectionSmallStyle);
        string[] questions = {
            "Was this decision in line with your values?",
            "Would you make the same decision in real life?",
            "Did you think about the impact on other people?",
            "Are you comfortable with this outcome?"
        };
        bool landscape = Screen.width > Screen.height;
        int columns = landscape ? 2 : 1;
        int rows = Mathf.CeilToInt(questions.Length / (float)columns);
        float gridTop = y + 98f;
        float gridBottom = y + height - 12f;
        const float rowGap = 8f;
        const float columnGap = 8f;
        float rowHeight = (gridBottom - gridTop - rowGap * (rows - 1)) / rows;
        float columnWidth = (width - 48f - columnGap * (columns - 1)) / columns;
        for (int i = 0; i < questions.Length; i++)
        {
            int row = i / columns;
            int column = i % columns;
            float columnX = x + 24f + column * (columnWidth + columnGap);
            float rowY = gridTop + row * (rowHeight + rowGap);
            float labelHeight = Mathf.Min(34f, Mathf.Max(24f, rowHeight - 52f));
            GUI.Label(new Rect(columnX, rowY, columnWidth, labelHeight), (i + 1) + ".  " + questions[i], reflectionSmallStyle);
            float buttonWidth = Mathf.Min(180f, (columnWidth - 24f) / 2f);
            float buttonY = rowY + labelHeight + 4f;
            float answerButtonHeight = Mathf.Max(48f, Mathf.Min(64f, rowY + rowHeight - buttonY));
            float answerX = columnX + (columnWidth - buttonWidth * 2f - 8f) / 2f;
            GUI.enabled = !reflectionSubmitted && !isSubmittingReflection;
            if (GUI.Button(new Rect(answerX, buttonY, buttonWidth, answerButtonHeight), "Yes", reflectionAnswers[i] == true ? reflectionSelectedButtonStyle : reflectionButtonStyle)) reflectionAnswers[i] = true;
            if (GUI.Button(new Rect(answerX + buttonWidth + 8f, buttonY, buttonWidth, answerButtonHeight), "No", reflectionAnswers[i] == false ? reflectionSelectedButtonStyle : reflectionButtonStyle)) reflectionAnswers[i] = false;
        }
        bool complete = Array.TrueForAll(reflectionAnswers, answer => answer.HasValue);
        GUI.enabled = complete && !reflectionSubmitted && !isSubmittingReflection;
        Rect submitRect = new Rect((Screen.width - Mathf.Min(420f, Screen.width - 48f)) / 2f, Screen.height * 0.82f, Mathf.Min(420f, Screen.width - 48f), 58f);
        if (GUI.Button(submitRect, isSubmittingReflection ? "SENDING…" : complete ? "SEND REFLECTION" : "ANSWER THE 4 QUESTIONS", reflectionButtonStyle) && complete)
            StartCoroutine(SubmitReflection());
        GUI.enabled = true;
        string reflectionStatus = reflectionMessage;
        if (finalDecision == "continue")
        {
            if (!string.IsNullOrEmpty(reflectionStatus))
                reflectionStatus += "\n";
            reflectionStatus += "The teacher chose to continue. You can finish your answers.";
        }
        float reflectionStatusY = submitRect.yMax + 6f;
        float reflectionStatusHeight = Mathf.Max(0f, Screen.height - reflectionStatusY - 8f);
        if (!string.IsNullOrEmpty(reflectionStatus) && reflectionStatusHeight > 0f)
            GUI.Label(new Rect(24f, reflectionStatusY, Screen.width - 48f, reflectionStatusHeight), reflectionStatus, reflectionSmallStyle);
    }

    private void DrawReflectionWaitScreen()
    {
        GUI.depth = -100;
        GUI.color = new Color(0.97f, 0.97f, 0.97f, 1f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(24, 18, Screen.width - 48, 48), "Serena  ·  Reflection time", reflectionTextStyle);

        float width = Mathf.Min(Screen.width - 48, 760f);
        float height = Mathf.Min(Screen.height * 0.48f, 360f);
        float x = (Screen.width - width) / 2f;
        float y = (Screen.height - height) / 2f;
        GUI.Box(new Rect(x, y, width, height), GUIContent.none, emotionCardStyle);
        GUI.Label(new Rect(x + 32, y + 36, width - 64, 64), "✓", reflectionTextStyle);

        string title = finalDecision == "continue" ? "The teacher chose to continue" : "Answers sent";
        string message = finalDecision == "continue"
            ? "Keep your tablet ready for the next part of the activity."
            : "Your answers were recorded without your name. Please wait while the teacher reviews the group's results.";
        GUI.Label(new Rect(x + 32, y + 112, width - 64, 58), title, reflectionTextStyle);
        GUI.Label(new Rect(x + 48, y + 184, width - 96, height - 210), message, reflectionSmallStyle);
    }

    private string GetReflectionContextPrompt()
    {
        bool hasDecision = selectedDecisionIndex >= 0 && selectedDecisionIndex < DecisionValues.Length;
        bool hasEmotion = selectedEmotionIndex >= 0 && selectedEmotionIndex < EmotionValues.Length;
        if (hasDecision && hasEmotion)
            return "You decided “" + DecisionValues[selectedDecisionIndex] + "” and felt “"
                + EmotionValues[selectedEmotionIndex] + "”. Answer with that decision in mind.";
        if (hasDecision)
            return "You decided “" + DecisionValues[selectedDecisionIndex] + "”. Answer with that decision in mind.";
        return "Think about your decision and its consequences for the people involved.";
    }

    private IEnumerator SubmitReflection()
    {
        if (isSubmittingReflection || reflectionSubmitted
            || string.IsNullOrWhiteSpace(StudentSessionContext.SessionId)
            || string.IsNullOrWhiteSpace(StudentSessionContext.StudentId))
            yield break;

        isSubmittingReflection = true;
        reflectionMessage = "Sending reflection…";
        string answers = string.Join(",", Array.ConvertAll(reflectionAnswers, answer => answer == true ? "yes" : "no"));
        string baseUrl = SerenaServerConfig.BaseUrl;
        string url = baseUrl + "/api/sessions/" + StudentSessionContext.SessionId
            + "/students/" + StudentSessionContext.StudentId + "/reflection";
        string json = JsonUtility.ToJson(new ReflectionRequest { answers = answers });
        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 8;
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            reflectionSubmitted = true;
            reflectionMessage = "Reflection sent. Wait for the teacher's decision.";
            PlayConfirmationTone();
        }
        else
        {
            reflectionMessage = "Could not send. Check the connection and try again.";
            Debug.LogWarning("Serena: fallo al enviar la reflexión (" + request.responseCode + "): " + request.error);
        }
        isSubmittingReflection = false;
    }    private void DrawInteractionStatus()
    {
        if (string.IsNullOrEmpty(interactionMessage))
            return;

        GUI.Label(new Rect(24, Screen.height * 0.91f, Screen.width - 48, Screen.height * 0.06f),
            interactionMessage, statusStyle);
    }

    // Paleta de la votación (misma identidad que la web del docente).
    private static readonly Color VoteRed = new Color(0.82f, 0.12f, 0.16f);
    private static readonly Color VoteGreen = new Color(0.13f, 0.71f, 0.45f);
    private static readonly Color VoteInk = new Color(0.07f, 0.07f, 0.09f);
    private static readonly Color VoteMuted = new Color(0.42f, 0.42f, 0.46f);
    private static readonly Color VoteLine = new Color(0.92f, 0.92f, 0.93f);

    private Font voteBoldFont;
    private Font voteRegularFont;
    private Texture2D voteCardTexture;
    private Texture2D voteCardContinueTexture;
    private Texture2D voteCardAlternativeTexture;
    private Texture2D voteCardDisabledTexture;
    private Texture2D voteContinueIcon;
    private Texture2D voteAlternativeIcon;
    private GUIStyle voteCardStyle;
    private GUIStyle voteCardContinueStyle;
    private GUIStyle voteCardAlternativeStyle;
    private GUIStyle voteCardDisabledStyle;
    private GUIStyle voteBrandStyle;
    private GUIStyle voteTitleStyle;
    private GUIStyle voteSubtitleStyle;
    private GUIStyle voteOptionTitleStyle;
    private GUIStyle voteOptionTextStyle;
    private GUIStyle voteFootnoteStyle;
    private GUIStyle voteStatusStyle;
    private float voteStylesScale = -1f;

    private void EnsureVoteStyles(float scale)
    {
        if (voteCardTexture == null)
        {
            voteBoldFont = Resources.Load<Font>("Fonts/Gabarito-Bold");
            voteRegularFont = Resources.Load<Font>("Fonts/Gabarito-Regular");
            voteCardTexture = CreateRoundedCardTexture(Color.white, VoteInk);
            voteCardContinueTexture = CreateRoundedCardTexture(new Color(0.91f, 0.98f, 0.94f), VoteGreen);
            voteCardAlternativeTexture = CreateRoundedCardTexture(new Color(0.99f, 0.92f, 0.92f), VoteRed);
            voteCardDisabledTexture = CreateRoundedCardTexture(Color.white, new Color(0.82f, 0.82f, 0.84f));
            voteContinueIcon = CreateVoteIcon(VoteGreen, false);
            voteAlternativeIcon = CreateVoteIcon(VoteRed, true);
        }

        if (Mathf.Approximately(scale, voteStylesScale))
            return;
        voteStylesScale = scale;

        GUIStyle Card(Texture2D background)
        {
            var style = new GUIStyle(GUIStyle.none);
            style.normal.background = background;
            style.border = new RectOffset(18, 18, 18, 18);
            return style;
        }
        voteCardStyle = Card(voteCardTexture);
        voteCardContinueStyle = Card(voteCardContinueTexture);
        voteCardAlternativeStyle = Card(voteCardAlternativeTexture);
        voteCardDisabledStyle = Card(voteCardDisabledTexture);

        GUIStyle Text(Font font, int size, Color color, TextAnchor anchor)
        {
            var style = new GUIStyle(GUI.skin.label);
            if (font != null) style.font = font;
            style.fontSize = Mathf.RoundToInt(size * scale);
            style.normal.textColor = color;
            style.alignment = anchor;
            style.wordWrap = true;
            style.padding = new RectOffset(0, 0, 0, 0);
            style.margin = new RectOffset(0, 0, 0, 0);
            return style;
        }
        voteBrandStyle = Text(voteBoldFont, 26, VoteRed, TextAnchor.MiddleLeft);
        voteTitleStyle = Text(voteBoldFont, 46, VoteRed, TextAnchor.MiddleCenter);
        voteSubtitleStyle = Text(voteRegularFont, 24, VoteInk, TextAnchor.MiddleCenter);
        voteOptionTitleStyle = Text(voteBoldFont, 27, VoteInk, TextAnchor.LowerLeft);
        voteOptionTextStyle = Text(voteRegularFont, 18, VoteMuted, TextAnchor.UpperLeft);
        voteFootnoteStyle = Text(voteRegularFont, 17, VoteMuted, TextAnchor.MiddleCenter);
        voteStatusStyle = Text(voteBoldFont, 20, VoteInk, TextAnchor.MiddleCenter);
    }

    private void DrawVotingPanel()
    {
        GUI.depth = -100;

        // H.U. 9.1.1 (Bryan): pantalla clara y adaptable; las tarjetas tienen el ancho de la columna
        // central y una altura táctil amplia tanto en horizontal como en vertical.
        float screenWidth = Mathf.Max(1f, Screen.width);
        float screenHeight = Mathf.Max(1f, Screen.height);
        float scale = Mathf.Clamp(Mathf.Min(screenWidth / 1280f, screenHeight / 800f), 0.65f, 2.2f);
        EnsureVoteStyles(scale);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(0, 0, screenWidth, screenHeight), Texture2D.whiteTexture);

        // Encabezado con la marca y una línea fina, como en la web del docente.
        float headerHeight = 64f * scale;
        GUI.Label(new Rect(28f * scale, 0f, 300f * scale, headerHeight), "Serena", voteBrandStyle);
        GUI.color = VoteLine;
        GUI.DrawTexture(new Rect(0f, headerHeight, screenWidth, Mathf.Max(1f, scale)), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float columnWidth = Mathf.Min(screenWidth - 48f * scale, 760f * scale);
        float x = (screenWidth - columnWidth) / 2f;
        float titleHeight = 60f * scale;
        float subtitleHeight = 36f * scale;
        float cardHeight = 118f * scale;
        float gap = 22f * scale;
        float footHeight = 30f * scale;
        float statusHeight = 34f * scale;
        float blockHeight = titleHeight + 8f * scale + subtitleHeight + gap * 1.6f
            + cardHeight * 2f + gap + gap + footHeight + statusHeight;
        float y = headerHeight + Mathf.Max(16f * scale, (screenHeight - headerHeight - blockHeight) / 2f);

        GUI.Label(new Rect(x, y, columnWidth, titleHeight), "Your decision matters", voteTitleStyle);
        y += titleHeight + 8f * scale;
        GUI.Label(new Rect(x, y, columnWidth, subtitleHeight), "What should the group do now?", voteSubtitleStyle);
        y += subtitleHeight + gap * 1.6f;

        bool canVote = !voteSubmitted && !isSubmittingVote && !voteLockedByConflict;
        Rect continueRect = new Rect(x, y, columnWidth, cardHeight);
        Rect alternativeRect = new Rect(x, continueRect.yMax + gap, columnWidth, cardHeight);

        if (DrawVoteOption(continueRect, "continue", voteContinueIcon,
                "Continue to the next narrative", "Move forward with the consequences of this decision",
                voteCardContinueStyle, canVote, scale))
        {
            submittedVoteChoice = "continue";
            StartCoroutine(SubmitVote("continue"));
        }
        if (DrawVoteOption(alternativeRect, "alternative", voteAlternativeIcon,
                "Vote to explore another alternative", "Go back and take a different path",
                voteCardAlternativeStyle, canVote, scale))
        {
            submittedVoteChoice = "alternative";
            StartCoroutine(SubmitVote("alternative"));
        }

        y = alternativeRect.yMax + gap;
        GUI.Label(new Rect(x, y, columnWidth, footHeight),
            "Your vote is individual. The teacher makes the final decision with the group's votes.", voteFootnoteStyle);
        y += footHeight + 6f * scale;

        string status;
        Color statusColor = VoteInk;
        if (voteSubmitted)
        {
            status = "Vote sent · Group: " + continueVotes + " continue · " + alternativeVotes + " explore an alternative";
            statusColor = new Color(0.08f, 0.5f, 0.31f);
        }
        else if (isSubmittingVote)
            status = "Sending vote…";
        else
        {
            status = voteMessage;
            if (!string.IsNullOrEmpty(status)) statusColor = VoteRed;
        }
        if (!string.IsNullOrEmpty(status))
        {
            voteStatusStyle.normal.textColor = statusColor;
            GUI.Label(new Rect(x - 40f * scale, y, columnWidth + 80f * scale, statusHeight), status, voteStatusStyle);
        }
    }

    // Dibuja una tarjeta de voto con su icono, título y descripción. Devuelve true al tocarla.
    private bool DrawVoteOption(Rect rect, string choice, Texture2D icon, string title, string text,
        GUIStyle selectedStyle, bool interactive, float scale)
    {
        bool isSelected = submittedVoteChoice == choice;
        bool dimmed = !interactive && !isSelected;
        GUIStyle cardStyle = isSelected ? selectedStyle : dimmed ? voteCardDisabledStyle : voteCardStyle;
        GUI.Box(rect, GUIContent.none, cardStyle);

        float iconSize = 64f * scale;
        float pad = 26f * scale;
        GUI.color = dimmed ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
        GUI.DrawTexture(new Rect(rect.x + pad, rect.center.y - iconSize / 2f, iconSize, iconSize), icon, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        float textX = rect.x + pad + iconSize + 22f * scale;
        float textWidth = rect.xMax - pad - textX;
        Color titleColor = voteOptionTitleStyle.normal.textColor;
        Color bodyColor = voteOptionTextStyle.normal.textColor;
        if (dimmed)
        {
            voteOptionTitleStyle.normal.textColor = new Color(0.62f, 0.62f, 0.65f);
            voteOptionTextStyle.normal.textColor = new Color(0.72f, 0.72f, 0.75f);
        }
        GUI.Label(new Rect(textX, rect.y, textWidth, rect.height * 0.52f), title, voteOptionTitleStyle);
        GUI.Label(new Rect(textX, rect.y + rect.height * 0.56f, textWidth, rect.height * 0.4f), text, voteOptionTextStyle);
        voteOptionTitleStyle.normal.textColor = titleColor;
        voteOptionTextStyle.normal.textColor = bodyColor;

        if (!interactive)
            return false;
        return GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    // Icono cuadrado redondeado con una flecha (continuar) o una flecha circular (explorar otra opción).
    private static Texture2D CreateVoteIcon(Color background, bool circular)
    {
        const int size = 128;
        const float radius = 24f;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        Vector2 center = new Vector2(size / 2f, size / 2f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float qx = Mathf.Abs(p.x - center.x) - (size / 2f - radius);
                float qy = Mathf.Abs(p.y - center.y) - (size / 2f - radius);
                float box = Mathf.Min(Mathf.Max(qx, qy), 0f) + new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude - radius;
                if (box > 0.5f) { texture.SetPixel(x, y, Color.clear); continue; }

                float glyph = circular ? CircularArrowDistance(p, center) : StraightArrowDistance(p, center);
                float ink = Mathf.Clamp01(0.5f - glyph);
                float edge = Mathf.Clamp01(0.5f - box);
                Color pixel = Color.Lerp(background, Color.white, ink);
                pixel.a = edge;
                texture.SetPixel(x, y, pixel);
            }
        }
        texture.Apply(false, true);
        return texture;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    private static float StraightArrowDistance(Vector2 p, Vector2 c)
    {
        const float half = 5f;
        float shaft = SegmentDistance(p, c + new Vector2(-24f, 0f), c + new Vector2(22f, 0f));
        float upper = SegmentDistance(p, c + new Vector2(24f, 0f), c + new Vector2(6f, 18f));
        float lower = SegmentDistance(p, c + new Vector2(24f, 0f), c + new Vector2(6f, -18f));
        return Mathf.Min(shaft, Mathf.Min(upper, lower)) - half;
    }

    private static float CircularArrowDistance(Vector2 p, Vector2 c)
    {
        const float half = 5f;
        const float ringRadius = 23f;
        const float gapStart = 95f;
        const float gapEnd = 150f;
        Vector2 d = p - c;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
        if (angle < 0f) angle += 360f;
        // Anillo abierto arriba a la izquierda; la punta sigue la tangente al final del arco.
        float ring = angle > gapStart && angle < gapEnd ? 999f : Mathf.Abs(d.magnitude - ringRadius) - half;
        float theta = gapStart * Mathf.Deg2Rad;
        Vector2 radial = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
        Vector2 tangent = new Vector2(-radial.y, radial.x);
        Vector2 end = c + radial * ringRadius;
        Vector2 tip = end + tangent * 10f;
        float head = Mathf.Min(SegmentDistance(p, tip, end - tangent * 4f + radial * 13f),
                               SegmentDistance(p, tip, end - tangent * 4f - radial * 13f)) - half;
        return Mathf.Min(ring, head);
    }

    private IEnumerator SubmitVote(string choice)
    {
        if (isSubmittingVote || voteSubmitted || voteLockedByConflict
            || string.IsNullOrWhiteSpace(StudentSessionContext.SessionId)
            || string.IsNullOrWhiteSpace(StudentSessionContext.StudentId))
            yield break;

        isSubmittingVote = true;
        voteMessage = string.Empty;
        string baseUrl = SerenaServerConfig.BaseUrl;
        string url = baseUrl + "/api/sessions/" + StudentSessionContext.SessionId
            + "/students/" + StudentSessionContext.StudentId + "/vote";
        string json = JsonUtility.ToJson(new VoteRequest { choice = choice });
        using var request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 8;
        yield return request.SendWebRequest();
        if (request.result == UnityWebRequest.Result.Success)
        {
            voteSubmitted = true;
            voteMessage = "Decision sent";
            PlayConfirmationTone();
        }
        else if (request.responseCode == 409)
        {
            // Un conflicto indica que la ronda cerró o que ya existía un voto; no prueba
            // que esta solicitud se aceptara, así que no se marca como enviada.
            voteLockedByConflict = true;
            submittedVoteChoice = string.Empty;
            voteMessage = "Voting is closed or your vote was already recorded.";
        }
        else
        {
            submittedVoteChoice = string.Empty;
            voteMessage = "Could not send the vote. Check the connection and try again.";
        }
        isSubmittingVote = false;
    }

    private string GetNeutralConsequenceText()
    {
        string consequence = selectedDecisionIndex switch
        {
            0 => "Dialogue can open a space to talk about what happened and hear different perspectives from the people involved.",
            1 => "By not stepping in, the situation may go on without an immediate conversation; each person may read that silence differently.",
            2 => "Sharing a rumor can spread the information to more people and make those involved feel exposed.",
            3 => "Changing the subject can stop the immediate conversation, but what happened may never be cleared up.",
            _ => "The group's decision may have shaped how the people involved experienced the situation. Think about what happened and how they may have felt."
        };

        return "Possible consequence: " + consequence;
    }

    private void DrawTransitionFeedback()
    {
        // Los mensajes positivos ahora los muestra StudentProgressView (H.U. 10.5.1).
        float elapsed = Time.unscaledTime - transitionStartedAt;
        const float duration = 0.6f;
        if (elapsed < 0f || elapsed >= duration) return;
        float progress = elapsed / duration;
        float alpha = Mathf.Sin(progress * Mathf.PI) * 0.32f;
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void PlayConfirmationTone()
    {
        if (confirmationAudio == null)
            confirmationAudio = gameObject.GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        if (confirmationClip == null)
        {
            const int sampleRate = 22050;
            const int sampleCount = 2205;
            confirmationClip = AudioClip.Create("SerenaDecisionConfirmed", sampleCount, 1, sampleRate, false);
            float[] samples = new float[sampleCount];
            for (int i = 0; i < samples.Length; i++)
            {
                float envelope = 1f - i / (float)samples.Length;
                samples[i] = Mathf.Sin(2f * Mathf.PI * 740f * i / sampleRate) * 0.08f * envelope;
            }
            confirmationClip.SetData(samples, 0);
        }
        confirmationAudio.PlayOneShot(confirmationClip, 0.25f);
    }

    private IEnumerator SubmitInteraction(string kind, string value)
    {
        if (isSubmittingInteraction || string.IsNullOrWhiteSpace(StudentSessionContext.SessionId)
            || string.IsNullOrWhiteSpace(StudentSessionContext.StudentId))
        {
            interactionMessage = "Your session could not be identified. Please reconnect.";
            yield break;
        }

        isSubmittingInteraction = true;
        interactionMessage = "Sending answer…";
        string baseUrl = SerenaServerConfig.BaseUrl;
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
        {
            interactionMessage = "Answer sent successfully.";
            PlayConfirmationTone();
        }
        else if (request.responseCode == 422 && kind == "decision")
        {
            // H.U. 9.3.1: el servidor rechaza una opción que ya fue la más votada.
            int rejected = Array.IndexOf(DecisionValues, value);
            if (rejected >= 0)
                blockedDecisions[rejected] = true;
            if (selectedDecisionIndex == rejected)
                selectedDecisionIndex = -1;
            interactionMessage = "That option was already the most voted. Choose an alternative.";
        }
        else
        {
            interactionMessage = "Could not send. Check the connection and try again.";
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

        reflectionTextStyle = new GUIStyle(GUI.skin.label);
        reflectionTextStyle.fontSize = Mathf.Clamp(Screen.width / 36, 22, 36);
        reflectionTextStyle.fontStyle = FontStyle.Bold;
        reflectionTextStyle.alignment = TextAnchor.MiddleCenter;
        reflectionTextStyle.wordWrap = true;
        reflectionTextStyle.normal.textColor = new Color(0.12f, 0.12f, 0.12f);

        reflectionSmallStyle = new GUIStyle(GUI.skin.label);
        reflectionSmallStyle.fontSize = Mathf.Clamp(Screen.width / 72, 15, 22);
        reflectionSmallStyle.wordWrap = true;
        reflectionSmallStyle.normal.textColor = new Color(0.2f, 0.2f, 0.2f);
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
        if (emotionDisabledCardTexture == null)
            emotionDisabledCardTexture = CreateRoundedCardTexture(new Color(0.78f, 0.78f, 0.76f, 1f), new Color(0.57f, 0.56f, 0.53f, 1f));
        if (emotionDisabledSelectedCardTexture == null)
            emotionDisabledSelectedCardTexture = CreateRoundedCardTexture(new Color(0.79f, 0.84f, 0.8f, 1f), new Color(0.52f, 0.63f, 0.55f, 1f));

        string[] emotionImageNames = { "curiosidad", "fuera", "neutro", "confundido", "incomodo" };
        for (int i = 0; i < emotionImages.Length; i++)
        {
            if (emotionImages[i] == null)
                emotionImages[i] = Resources.Load<Texture2D>("Emociones/" + emotionImageNames[i]);
        }

        emotionCardStyle = new GUIStyle(GUI.skin.button);
        emotionCardStyle.normal.background = emotionCardTexture;
        emotionCardStyle.hover.background = emotionCardTexture;
        if (decisionPressedCardTexture == null)
            decisionPressedCardTexture = CreateRoundedCardTexture(new Color(0.47f, 0.31f, 0.28f, 1f), new Color(0.28f, 0.18f, 0.17f, 1f));
        emotionCardStyle.active.background = decisionPressedCardTexture;
        emotionCardStyle.border = new RectOffset(18, 18, 18, 18);
        emotionCardStyle.padding = new RectOffset(0, 0, 0, 0);
        emotionCardStyle.margin = new RectOffset(0, 0, 0, 0);
        emotionSelectedCardStyle = new GUIStyle(emotionCardStyle);
        emotionSelectedCardStyle.normal.background = emotionSelectedCardTexture;
        emotionSelectedCardStyle.hover.background = emotionSelectedCardTexture;
        emotionSelectedCardStyle.active.background = decisionPressedCardTexture;
        emotionCardStyle.fontSize = 18;
        emotionCardStyle.fontStyle = FontStyle.Bold;
        emotionCardStyle.alignment = TextAnchor.MiddleCenter;
        emotionCardStyle.normal.textColor = new Color(0.16f, 0.11f, 0.2f);
        emotionCardStyle.hover.textColor = emotionCardStyle.normal.textColor;
        emotionCardStyle.active.textColor = Color.white;
        emotionSelectedCardStyle.normal.textColor = new Color(0.1f, 0.42f, 0.27f);
        emotionSelectedCardStyle.hover.textColor = emotionSelectedCardStyle.normal.textColor;
        emotionSelectedCardStyle.active.textColor = emotionSelectedCardStyle.normal.textColor;

        emotionPressedCardStyle = new GUIStyle(emotionCardStyle);
        emotionPressedCardStyle.normal.background = decisionPressedCardTexture;
        emotionPressedCardStyle.hover.background = decisionPressedCardTexture;
        emotionPressedCardStyle.active.background = decisionPressedCardTexture;

        decisionCardStyle = new GUIStyle(emotionCardStyle);
        decisionCardStyle.fontSize = Mathf.Clamp(Screen.width / 34, 22, 38);
        decisionCardStyle.fontStyle = FontStyle.Bold;
        decisionCardStyle.alignment = TextAnchor.MiddleCenter;
        decisionCardStyle.wordWrap = true;
        decisionCardStyle.normal.textColor = new Color(0.16f, 0.11f, 0.2f);
        decisionCardStyle.hover.textColor = decisionCardStyle.normal.textColor;
        decisionCardStyle.active.textColor = Color.white;
        decisionCardStyle.active.background = decisionPressedCardTexture;

        decisionSelectedCardStyle = new GUIStyle(decisionCardStyle);
        decisionSelectedCardStyle.normal.background = emotionSelectedCardTexture;
        decisionSelectedCardStyle.hover.background = emotionSelectedCardTexture;
        decisionSelectedCardStyle.active.background = decisionPressedCardTexture;

        // GUIStyle no tiene estado "disabled" en Unity 6: se usa un estilo propio para lo deshabilitado.
        disabledCardStyle = new GUIStyle(decisionCardStyle);
        disabledCardStyle.normal.background = emotionDisabledCardTexture;
        disabledCardStyle.hover.background = emotionDisabledCardTexture;
        disabledCardStyle.active.background = emotionDisabledCardTexture;
        disabledCardStyle.normal.textColor = new Color(0.42f, 0.42f, 0.4f, 1f);
        disabledCardStyle.hover.textColor = disabledCardStyle.normal.textColor;
        disabledCardStyle.active.textColor = disabledCardStyle.normal.textColor;

        blockedDecisionTextStyle = new GUIStyle(GUI.skin.label);
        blockedDecisionTextStyle.fontSize = decisionCardStyle.fontSize;
        blockedDecisionTextStyle.fontStyle = FontStyle.Bold;
        blockedDecisionTextStyle.alignment = TextAnchor.MiddleCenter;
        blockedDecisionTextStyle.wordWrap = true;
        blockedDecisionTextStyle.normal.textColor = new Color(0.16f, 0.11f, 0.2f);

        blockedDecisionTagStyle = new GUIStyle(GUI.skin.label);
        blockedDecisionTagStyle.fontSize = Mathf.Clamp(Screen.width / 80, 13, 20);
        blockedDecisionTagStyle.fontStyle = FontStyle.Bold;
        blockedDecisionTagStyle.alignment = TextAnchor.MiddleCenter;
        blockedDecisionTagStyle.wordWrap = true;
        blockedDecisionTagStyle.normal.textColor = new Color(0.3f, 0.3f, 0.3f);

        consequenceTitleStyle = new GUIStyle(titleStyle);
        consequenceTitleStyle.alignment = TextAnchor.MiddleCenter;
        consequenceTextStyle = new GUIStyle(statusStyle);
        consequenceTextStyle.fontSize = Mathf.Clamp(Screen.width / 45, 18, 30);

        reflectionButtonStyle = new GUIStyle(emotionCardStyle);
        reflectionButtonStyle.fontSize = Mathf.Clamp(Screen.width / 75, 16, 24);
        reflectionButtonStyle.wordWrap = true;
        reflectionButtonStyle.normal.textColor = new Color(0.16f, 0.11f, 0.2f);
        reflectionSelectedButtonStyle = new GUIStyle(reflectionButtonStyle);
        reflectionSelectedButtonStyle.normal.background = emotionSelectedCardTexture;
        reflectionSelectedButtonStyle.hover.background = emotionSelectedCardTexture;
        reflectionSelectedButtonStyle.active.background = decisionPressedCardTexture;
        reflectionSelectedButtonStyle.normal.textColor = new Color(0.1f, 0.42f, 0.27f);

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
        if (emotionDisabledCardTexture != null)
            Destroy(emotionDisabledCardTexture);
        if (emotionDisabledSelectedCardTexture != null)
            Destroy(emotionDisabledSelectedCardTexture);
        if (decisionPressedCardTexture != null)
            Destroy(decisionPressedCardTexture);
        foreach (var texture in new[] { voteCardTexture, voteCardContinueTexture, voteCardAlternativeTexture,
                     voteCardDisabledTexture, voteContinueIcon, voteAlternativeIcon })
        {
            if (texture != null)
                Destroy(texture);
        }
    }
}
