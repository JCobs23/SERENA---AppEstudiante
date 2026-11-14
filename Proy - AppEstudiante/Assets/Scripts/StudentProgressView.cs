using UnityEngine;

// Progreso del estudiante en la tablet (Edwin, Sprint 4):
//  H.U. 10.1.1 línea de tiempo · 10.1.2 fracción y porcentaje · 10.1.3 actualización automática
//  H.U. 10.3.1 botón y menú de progreso · 10.4.1 barra · 10.4.2 información de la etapa
//  H.U. 10.5.1 mensajes positivos · 10.5.2 resalte suave al completar una etapa
// La etapa la publica el servidor (contrato de estado de sesión); NarrativeStageView la entrega
// en cada consulta mediante UpdateStage. Se dibuja por encima de la actividad (GUI.depth menor)
// para que, con el menú abierto, los toques no lleguen a los botones de la etapa.
public class StudentProgressView : MonoBehaviour
{
    public const int TotalStages = 5;

    private static readonly string[] StageNames =
        { "Story", "Emotion", "Decision", "Consequences", "Reflection" };

    // H.U. 10.4.2: texto breve de cada etapa. [Pendiente: validar la redacción con la docente.]
    private static readonly string[] StageInfo =
    {
        "Watch the story on the classroom screen and notice how the characters feel.",
        "Choose the emotion closest to what you feel right now.",
        "Think about the options and choose how you would act in this situation.",
        "See on your tablet what happened with the decision you made.",
        "Answer the questions and think about what you learned with the group."
    };

    // H.U. 10.5.1: mensajes positivos según la etapa que se acaba de completar.
    private static readonly string[][] PositiveMessages =
    {
        new[] { "Nice! Now you know the story.", "Thanks for paying attention to the story.", "Good start: you saw the whole situation." },
        new[] { "Recognizing what you feel is a big step.", "Thanks for sharing how you feel.", "Your emotion matters too." },
        new[] { "You made your decision calmly.", "Thanks for thinking about other people.", "Deciding is not always easy, and you did it." },
        new[] { "You saw what happened with your decision.", "Looking at the consequences helps you understand better.", "You're doing great! Only the last stage is left." }
    };

    private const float ToastDuration = 3.4f;
    private const float ToastFade = 0.35f;

    private NarrativeStageView stageView;
    private int stage;
    private string stageLabel = string.Empty;
    private float displayedProgress;
    private bool menuOpen;
    private string toastTitle = string.Empty;
    private string toastMessage = string.Empty;
    private float toastStartedAt = -10f;
    private string lastPositiveMessage = string.Empty;
    private AudioSource chimeSource;
    private AudioClip chimeClip;

    private Texture2D panelTexture;
    private Texture2D circleTexture;
    private GUIStyle hudStyle;
    private GUIStyle hudTextStyle;
    private GUIStyle hudHintStyle;
    private GUIStyle titleStyle;
    private GUIStyle bodyStyle;
    private GUIStyle smallStyle;
    private GUIStyle stepLabelStyle;
    private GUIStyle stepMarkStyle;
    private GUIStyle closeButtonStyle;

    public bool IsMenuOpen => menuOpen;

    public void Initialize(NarrativeStageView view)
    {
        stageView = view;
    }

    // H.U. 10.1.3: se llama con cada estado del servidor. La primera sincronización (al entrar
    // a la escena o tras reconectar) fija el progreso sin animaciones ni mensajes, para evitar
    // saltos; los avances posteriores se animan y se celebran.
    public void UpdateStage(int newStage, string label, bool exploringAlternative)
    {
        int previous = stage;
        stage = newStage;
        stageLabel = label ?? string.Empty;

        if (newStage < 1 || newStage > TotalStages)
        {
            menuOpen = false;
            toastStartedAt = -10f;
            displayedProgress = 0f;
            return;
        }

        if (previous < 1)
        {
            displayedProgress = TargetProgress(newStage);
            return;
        }

        if (newStage == previous)
            return;

        if (newStage > previous)
        {
            // H.U. 10.5.2: se reconoce la última etapa completada, aunque el docente avance varias seguidas.
            CelebrateCompleted(newStage - 1);
        }
        else if (exploringAlternative)
        {
            ShowToast("Exploring another alternative", "You are back at the decision stage with new options.");
        }
        else
        {
            ShowToast("Back to stage " + newStage, StageInfo[newStage - 1]);
        }
    }

    private static float TargetProgress(int value) => Mathf.Clamp01(value / (float)TotalStages);

    private void CelebrateCompleted(int completedStage)
    {
        if (completedStage < 1 || completedStage > PositiveMessages.Length)
            return;

        string[] pool = PositiveMessages[completedStage - 1];
        // H.U. 10.5.1: nunca se repite el mismo mensaje dos veces seguidas.
        string message = pool[Random.Range(0, pool.Length)];
        if (message == lastPositiveMessage)
            message = pool[(System.Array.IndexOf(pool, message) + 1) % pool.Length];
        lastPositiveMessage = message;

        ShowToast("✓ Stage " + completedStage + " completed: " + StageNames[completedStage - 1], message);
        PlayChime();
    }

    private void ShowToast(string title, string message)
    {
        toastTitle = title;
        toastMessage = message;
        toastStartedAt = Time.unscaledTime;
    }

    private void Update()
    {
        if (stage < 1)
            return;
        // Animación suave de la barra hacia la etapa actual (H.U. 10.1.3 y 10.4.1).
        displayedProgress = Mathf.MoveTowards(displayedProgress, TargetProgress(stage), Time.unscaledDeltaTime * 0.6f);
    }

    private void OnGUI()
    {
        if (stage < 1 || stage > TotalStages)
            return;

        EnsureStyles();
        GUI.depth = -150;

        if (menuOpen)
        {
            DrawMenu();
            // Con el menú abierto, ningún toque llega a la actividad que queda detrás.
            Event current = Event.current;
            if (current.isMouse || current.type == EventType.ScrollWheel || current.type == EventType.TouchDown
                || current.type == EventType.TouchUp || current.type == EventType.TouchMove)
                current.Use();
            return;
        }

        DrawToast();
        DrawHud();
    }

    // H.U. 10.3.1, 10.1.2 y 10.4.1: botón siempre visible con la etapa, el porcentaje y una barra.
    private void DrawHud()
    {
        Rect safe = Screen.safeArea;
        float width = Mathf.Clamp(Screen.width * 0.3f, 240f, 340f);
        const float height = 52f;
        float bottomInset = safe.yMin;
        float reserve = stage == 4 ? Mathf.Clamp(Screen.height * 0.055f, 28f, 58f) + 40f : 0f;
        Rect rect = new Rect(safe.xMin + 12f, Screen.height - bottomInset - reserve - height - 10f, width, height);

        if (GUI.Button(rect, GUIContent.none, hudStyle))
            menuOpen = true;

        GUI.Label(new Rect(rect.x + 14f, rect.y + 6f, rect.width - 28f, 22f),
            "Stage " + stage + " of " + TotalStages + " · " + Mathf.RoundToInt(TargetProgress(stage) * 100f) + "%", hudTextStyle);
        GUI.Label(new Rect(rect.x + 14f, rect.y + 6f, rect.width - 28f, 22f), "View progress ▸", hudHintStyle);
        DrawBar(new Rect(rect.x + 14f, rect.y + 34f, rect.width - 28f, 7f), displayedProgress);
    }

    private void DrawMenu()
    {
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);

        float width = Mathf.Min(Screen.width - 48f, 760f);
        float height = Mathf.Min(Screen.height - 48f, 600f);
        Rect panel = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);
        GUI.color = new Color(0.08f, 0.08f, 0.09f, 1f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = Color.white;

        float pad = Mathf.Clamp(width * 0.05f, 18f, 36f);
        float x = panel.x + pad;
        float inner = width - pad * 2f;
        float y = panel.y + pad;

        GUI.Label(new Rect(x, y, inner, 40f), "Your progress", titleStyle);
        y += 44f;

        // H.U. 10.1.2: fracción, porcentaje y lo que falta.
        int remaining = TotalStages - stage;
        GUI.Label(new Rect(x, y, inner, 30f),
            "Stage " + stage + " of " + TotalStages + " · " + Mathf.RoundToInt(TargetProgress(stage) * 100f) + "% of the way", bodyStyle);
        y += 32f;
        DrawBar(new Rect(x, y, inner, 12f), displayedProgress);
        y += 20f;
        GUI.Label(new Rect(x, y, inner, 26f), remaining == 0
            ? "You are on the last stage of the experience."
            : remaining == 1 ? "1 stage left to finish." : remaining + " stages left to finish.", smallStyle);
        y += 40f;

        // H.U. 10.1.1: línea de tiempo con las cinco etapas.
        float stepWidth = inner / TotalStages;
        float circle = Mathf.Min(44f, stepWidth * 0.5f);
        float lineY = y + circle / 2f;
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(new Rect(x + stepWidth / 2f, lineY - 2f, stepWidth * (TotalStages - 1), 4f), Texture2D.whiteTexture);
        GUI.color = new Color(0.12f, 0.72f, 0.48f, 1f);
        GUI.DrawTexture(new Rect(x + stepWidth / 2f, lineY - 2f, stepWidth * Mathf.Max(0, stage - 1), 4f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        for (int i = 1; i <= TotalStages; i++)
        {
            float cx = x + stepWidth * (i - 0.5f);
            Rect dot = new Rect(cx - circle / 2f, y, circle, circle);
            bool done = i < stage;
            bool current = i == stage;
            GUI.color = done ? new Color(0.12f, 0.72f, 0.48f, 1f)
                : current ? new Color(0.84f, 0.11f, 0.15f, 1f) : new Color(1f, 1f, 1f, 0.3f);
            GUI.DrawTexture(dot, circleTexture);
            GUI.color = Color.white;
            GUI.Label(dot, done ? "✓" : i.ToString(), stepMarkStyle);
            GUI.color = current ? Color.white : new Color(1f, 1f, 1f, done ? 0.85f : 0.55f);
            GUI.Label(new Rect(cx - stepWidth / 2f, y + circle + 6f, stepWidth, 40f), StageNames[i - 1], stepLabelStyle);
            GUI.color = Color.white;
        }
        y += circle + 56f;

        // H.U. 10.4.2: información breve de la etapa actual.
        string heading = "Now: " + (string.IsNullOrEmpty(stageLabel) ? StageNames[stage - 1] : stageLabel);
        float infoHeight = Mathf.Max(60f, bodyStyle.CalcHeight(new GUIContent(StageInfo[stage - 1]), inner - 24f) + 44f);
        GUI.color = new Color(1f, 1f, 1f, 0.08f);
        GUI.DrawTexture(new Rect(x, y, inner, infoHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 12f, y + 8f, inner - 24f, 28f), heading, hudTextStyle);
        GUI.Label(new Rect(x + 12f, y + 36f, inner - 24f, infoHeight - 40f), StageInfo[stage - 1], bodyStyle);

        Rect close = new Rect(panel.center.x - Mathf.Min(170f, inner / 2f), panel.yMax - pad - 54f, Mathf.Min(340f, inner), 54f);
        if (GUI.Button(close, "BACK TO THE ACTIVITY", closeButtonStyle))
            menuOpen = false;
        // Tocar fuera del panel también cierra el menú.
        if (Event.current.type == EventType.MouseUp && !panel.Contains(Event.current.mousePosition))
            menuOpen = false;
    }

    // H.U. 10.5.1 y 10.5.2: aviso breve, con entrada y salida suaves, que no bloquea la actividad.
    private void DrawToast()
    {
        float elapsed = Time.unscaledTime - toastStartedAt;
        if (elapsed < 0f || elapsed > ToastDuration || string.IsNullOrEmpty(toastTitle))
            return;

        float alpha = Mathf.Min(1f, elapsed / ToastFade, (ToastDuration - elapsed) / ToastFade);
        // Esquina inferior derecha: no tapa títulos, tarjetas ni el botón de confirmar.
        Rect safe = Screen.safeArea;
        float width = Mathf.Clamp(Screen.width * 0.36f, 260f, 520f);
        float messageHeight = bodyStyle.CalcHeight(new GUIContent(toastMessage), width - 32f);
        float height = 46f + messageHeight;
        float reserve = stage == 4 ? Mathf.Clamp(Screen.height * 0.055f, 28f, 58f) + 40f : 0f;
        Rect rect = new Rect(safe.xMax - width - 12f,
            Screen.height - safe.yMin - reserve - height - 10f + (1f - alpha) * 12f, width, height);

        GUI.color = new Color(1f, 1f, 1f, alpha);
        GUI.DrawTexture(rect, panelTexture);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 8f, rect.width - 32f, 28f), toastTitle, hudTextStyle);
        GUI.Label(new Rect(rect.x + 16f, rect.y + 38f, rect.width - 32f, messageHeight), toastMessage, bodyStyle);
        GUI.color = Color.white;
    }

    private static void DrawBar(Rect rect, float progress)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.22f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = new Color(0.84f, 0.11f, 0.15f, 1f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(progress), rect.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // H.U. 10.5.2: dos notas suaves y cortas. No suena si el video de la tablet está reproduciendo audio.
    private void PlayChime()
    {
        if (stageView != null && stageView.IsFullscreenVideoPlaying)
            return;

        if (chimeSource == null)
        {
            chimeSource = gameObject.AddComponent<AudioSource>();
            chimeSource.playOnAwake = false;
        }
        if (chimeClip == null)
        {
            const int sampleRate = 22050;
            int noteLength = Mathf.RoundToInt(sampleRate * 0.16f);
            float[] samples = new float[noteLength * 2];
            float[] notes = { 523.25f, 783.99f };
            for (int n = 0; n < 2; n++)
            for (int i = 0; i < noteLength; i++)
            {
                float t = i / (float)sampleRate;
                float envelope = Mathf.Sin(Mathf.PI * i / noteLength);
                samples[n * noteLength + i] = Mathf.Sin(2f * Mathf.PI * notes[n] * t) * 0.06f * envelope;
            }
            chimeClip = AudioClip.Create("SerenaStageCompleted", samples.Length, 1, sampleRate, false);
            chimeClip.SetData(samples, 0);
        }
        chimeSource.PlayOneShot(chimeClip, 0.2f);
    }

    private void EnsureStyles()
    {
        if (hudStyle != null)
            return;

        panelTexture = new Texture2D(1, 1);
        panelTexture.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.09f, 0.94f));
        panelTexture.Apply();

        const int size = 64;
        circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int py = 0; py < size; py++)
        for (int px = 0; px < size; px++)
        {
            float d = Vector2.Distance(new Vector2(px + 0.5f, py + 0.5f), new Vector2(size / 2f, size / 2f));
            circleTexture.SetPixel(px, py, new Color(1f, 1f, 1f, Mathf.Clamp01(size / 2f - d)));
        }
        circleTexture.Apply();

        hudStyle = new GUIStyle(GUI.skin.button);
        hudStyle.normal.background = panelTexture;
        hudStyle.hover.background = panelTexture;
        hudStyle.active.background = panelTexture;

        hudTextStyle = new GUIStyle(GUI.skin.label);
        hudTextStyle.fontSize = Mathf.Clamp(Screen.width / 70, 15, 22);
        hudTextStyle.fontStyle = FontStyle.Bold;
        hudTextStyle.normal.textColor = Color.white;

        hudHintStyle = new GUIStyle(hudTextStyle);
        hudHintStyle.fontStyle = FontStyle.Normal;
        hudHintStyle.fontSize = Mathf.Clamp(Screen.width / 85, 12, 18);
        hudHintStyle.alignment = TextAnchor.UpperRight;
        hudHintStyle.normal.textColor = new Color(1f, 1f, 1f, 0.7f);

        titleStyle = new GUIStyle(GUI.skin.label);
        titleStyle.fontSize = Mathf.Clamp(Screen.width / 40, 22, 34);
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.normal.textColor = Color.white;

        bodyStyle = new GUIStyle(GUI.skin.label);
        bodyStyle.fontSize = Mathf.Clamp(Screen.width / 68, 15, 22);
        bodyStyle.wordWrap = true;
        bodyStyle.normal.textColor = new Color(0.92f, 0.92f, 0.92f);

        smallStyle = new GUIStyle(bodyStyle);
        smallStyle.fontSize = Mathf.Clamp(Screen.width / 80, 13, 19);
        smallStyle.normal.textColor = new Color(1f, 1f, 1f, 0.75f);

        stepLabelStyle = new GUIStyle(smallStyle);
        stepLabelStyle.alignment = TextAnchor.UpperCenter;
        stepLabelStyle.normal.textColor = Color.white;

        stepMarkStyle = new GUIStyle(hudTextStyle);
        stepMarkStyle.alignment = TextAnchor.MiddleCenter;

        closeButtonStyle = new GUIStyle(GUI.skin.button);
        closeButtonStyle.fontSize = Mathf.Clamp(Screen.width / 70, 15, 22);
        closeButtonStyle.fontStyle = FontStyle.Bold;
    }

    private void OnDestroy()
    {
        if (panelTexture != null)
            Destroy(panelTexture);
        if (circleTexture != null)
            Destroy(circleTexture);
    }
}
