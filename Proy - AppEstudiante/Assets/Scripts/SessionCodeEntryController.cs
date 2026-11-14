using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Controls the keypad objects authored in the IngresoCodigo scene.</summary>
public class SessionCodeEntryController : MonoBehaviour
{
    private const int CodeLength = 6;
    [SerializeField] private Button[] numberButtons;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_Text[] codeSlots;
    [SerializeField] private TMP_Text statusText;
    private string code = string.Empty;
    private bool isValidatingSession;
    private bool isServerSettingsOpen;
    private Canvas serverSettingsCanvas;
    private RectTransform serverSettingsSafeArea;
    private GameObject serverSettingsOverlay;
    private TMP_InputField serverUrlInput;
    private TMP_Text serverSettingsMessageText;
    private bool[] savedButtonInteractability;
    private Rect lastSafeArea;

    private void Start()
    {
        if (numberButtons != null)
        {
            for (int i = 0; i < numberButtons.Length; i++)
            {
                int digit = i == 9 ? 0 : i + 1;
                if (numberButtons[i] != null)
                {
                    StudentButtonDepthStyle.Apply(numberButtons[i]);
                    numberButtons[i].onClick.AddListener(() => AddDigit(digit));
                }
            }
        }

        if (deleteButton != null)
        {
            StudentButtonDepthStyle.Apply(deleteButton);
            deleteButton.onClick.AddListener(DeleteDigit);
        }
        if (joinButton != null)
        {
            StudentButtonDepthStyle.Apply(joinButton);
            joinButton.onClick.AddListener(JoinSession);
        }
        CreateServerSettingsUI();
        RefreshDisplay();
    }

    private void Update()
    {
        if (serverSettingsSafeArea != null && lastSafeArea != Screen.safeArea)
            ApplySafeArea();
    }

    private void OnDestroy()
    {
        if (numberButtons != null)
            foreach (Button button in numberButtons)
                if (button != null) button.onClick.RemoveAllListeners();

        if (deleteButton != null) deleteButton.onClick.RemoveListener(DeleteDigit);
        if (joinButton != null) joinButton.onClick.RemoveListener(JoinSession);
        if (isServerSettingsOpen)
            RestoreEntryButtons();
        if (serverSettingsCanvas != null)
            Destroy(serverSettingsCanvas.gameObject);
    }

    private void CreateServerSettingsUI()
    {
        GameObject canvasObject = new GameObject("SerenaServerSettingsCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        serverSettingsCanvas = canvasObject.GetComponent<Canvas>();
        serverSettingsCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        serverSettingsCanvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(900f, 1600f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        serverSettingsSafeArea = CreateRect("SafeArea", canvasRect);
        ApplySafeArea();

        Button openButton = CreateButton(serverSettingsSafeArea, "ServerSettingsButton", "Servidor", 190f, 62f);
        SetTopRight(openButton.GetComponent<RectTransform>(), 18f, 18f, 190f, 62f);
        openButton.onClick.AddListener(OpenServerSettings);

        serverSettingsOverlay = new GameObject("ServerSettingsOverlay", typeof(RectTransform), typeof(Image));
        RectTransform overlayRect = serverSettingsOverlay.GetComponent<RectTransform>();
        overlayRect.SetParent(canvasRect, false);
        Stretch(overlayRect);
        Image overlayImage = serverSettingsOverlay.GetComponent<Image>();
        overlayImage.color = new Color(0.05f, 0.04f, 0.04f, 0.72f);
        overlayImage.raycastTarget = true;

        GameObject panelObject = new GameObject("ServerSettingsPanel", typeof(RectTransform), typeof(Image));
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(overlayRect, false);
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(840f, 800f);
        panelObject.GetComponent<Image>().color = new Color32(250, 247, 241, 255);

        CreateLabel(panelRect, "Title", "Configuración del servidor", 32f,
            new Color32(55, 43, 40, 255), TextAlignmentOptions.Center, new Vector2(0f, -70f), new Vector2(760f, 64f));
        CreateLabel(panelRect, "Instructions",
            "Escribe la dirección base del servidor del aula. No incluyas rutas de API.", 24f,
            new Color32(76, 66, 62, 255), TextAlignmentOptions.Center, new Vector2(0f, -165f), new Vector2(740f, 100f));

        GameObject inputObject = new GameObject("ServerUrlInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
        RectTransform inputRect = inputObject.GetComponent<RectTransform>();
        inputRect.SetParent(panelRect, false);
        inputRect.anchorMin = inputRect.anchorMax = new Vector2(0.5f, 1f);
        inputRect.pivot = new Vector2(0.5f, 1f);
        inputRect.anchoredPosition = new Vector2(0f, -285f);
        inputRect.sizeDelta = new Vector2(740f, 76f);
        Image inputImage = inputObject.GetComponent<Image>();
        inputImage.color = Color.white;

        RectTransform textViewport = CreateRect("TextViewport", inputRect);
        textViewport.gameObject.AddComponent<RectMask2D>();
        StretchWithPadding(textViewport, 14f);
        TMP_Text inputText = CreateLabel(textViewport, "Text", string.Empty, 24f,
            new Color32(45, 40, 38, 255), TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
        Stretch(inputText.rectTransform);
        TMP_Text placeholder = CreateLabel(textViewport, "Placeholder", "http://servidor:puerto", 24f,
            new Color32(140, 132, 127, 255), TextAlignmentOptions.MidlineLeft, Vector2.zero, Vector2.zero);
        Stretch(placeholder.rectTransform);

        serverUrlInput = inputObject.GetComponent<TMP_InputField>();
        serverUrlInput.textViewport = textViewport;
        serverUrlInput.textComponent = inputText;
        serverUrlInput.placeholder = placeholder;
        serverUrlInput.targetGraphic = inputImage;
        serverUrlInput.lineType = TMP_InputField.LineType.SingleLine;
        serverUrlInput.keyboardType = TouchScreenKeyboardType.URL;
        serverUrlInput.contentType = TMP_InputField.ContentType.Standard;
        serverUrlInput.characterLimit = 512;
        serverUrlInput.shouldHideMobileInput = false;
        serverUrlInput.restoreOriginalTextOnEscape = false;

        serverSettingsMessageText = CreateLabel(panelRect, "Status", string.Empty, 22f,
            new Color32(76, 66, 62, 255), TextAlignmentOptions.Center, new Vector2(0f, -405f), new Vector2(740f, 100f));
        Button saveButton = CreateButton(panelRect, "SaveServerSettings", "Guardar", 340f, 74f);
        SetBottom(saveButton.GetComponent<RectTransform>(), 48f, 0f, 340f, 74f);
        saveButton.onClick.AddListener(SaveServerSettings);

        Button closeButton = CreateButton(panelRect, "CloseServerSettings", "Cerrar", 340f, 74f);
        SetBottom(closeButton.GetComponent<RectTransform>(), 48f, 1f, 340f, 74f);
        closeButton.onClick.AddListener(CloseServerSettings);
        serverSettingsOverlay.SetActive(false);
    }

    private void ApplySafeArea()
    {
        lastSafeArea = Screen.safeArea;
        serverSettingsSafeArea.anchorMin = new Vector2(lastSafeArea.xMin / Screen.width, lastSafeArea.yMin / Screen.height);
        serverSettingsSafeArea.anchorMax = new Vector2(lastSafeArea.xMax / Screen.width, lastSafeArea.yMax / Screen.height);
        serverSettingsSafeArea.offsetMin = Vector2.zero;
        serverSettingsSafeArea.offsetMax = Vector2.zero;
    }

    private void OpenServerSettings()
    {
        serverUrlInput.text = SerenaServerConfig.BaseUrl;
        SetServerSettingsMessage(string.Empty, false);
        SaveEntryButtonInteractability();
        isServerSettingsOpen = true;
        serverSettingsOverlay.SetActive(true);
    }

    private void SaveServerSettings()
    {
        if (!SerenaServerConfig.TrySave(serverUrlInput.text, out string normalized, out string message))
        {
            SetServerSettingsMessage(message, true);
            return;
        }

        serverUrlInput.text = normalized;
        SetServerSettingsMessage(message, false);
    }

    private void CloseServerSettings()
    {
        isServerSettingsOpen = false;
        serverSettingsOverlay.SetActive(false);
        RestoreEntryButtons();
    }

    private void SetServerSettingsMessage(string message, bool isError)
    {
        serverSettingsMessageText.text = message;
        serverSettingsMessageText.color = isError
            ? new Color32(165, 28, 38, 255)
            : new Color32(35, 112, 72, 255);
    }

    private Button CreateButton(Transform parent, string objectName, string label, float width, float height)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = new Vector2(width, height);
        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color32(190, 38, 48, 255);
        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = new Color32(190, 38, 48, 255);
        colors.highlightedColor = new Color32(210, 54, 64, 255);
        colors.pressedColor = new Color32(130, 25, 34, 255);
        colors.selectedColor = new Color32(210, 54, 64, 255);
        colors.disabledColor = new Color32(145, 110, 110, 180);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.transition = Selectable.Transition.ColorTint;
        TMP_Text text = CreateLabel(rect, "Label", label, 24f, Color.white,
            TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
        Stretch(text.rectTransform);
        return button;
    }

    private TMP_Text CreateLabel(Transform parent, string objectName, string text, float fontSize,
        Color color, TextAlignmentOptions alignment, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject labelObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        if (statusText != null && statusText.font != null)
            label.font = statusText.font;
        else
            label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = fontSize;
        label.color = color;
        label.alignment = alignment;
        label.enableAutoSizing = true;
        label.fontSizeMin = fontSize * 0.75f;
        label.fontSizeMax = fontSize;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void StretchWithPadding(RectTransform rect, float padding)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    private static void SetTopRight(RectTransform rect, float right, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-right, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void SetBottom(RectTransform rect, float inset, float horizontalAnchor, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(horizontalAnchor, 0f);
        rect.pivot = new Vector2(horizontalAnchor, 0f);
        rect.anchoredPosition = new Vector2(horizontalAnchor == 0f ? inset : -inset, inset);
        rect.sizeDelta = new Vector2(width, height);
    }

    private void SaveEntryButtonInteractability()
    {
        int count = numberButtons == null ? 0 : numberButtons.Length;
        savedButtonInteractability = new bool[count + 2];
        for (int i = 0; i < count; i++)
        {
            savedButtonInteractability[i] = numberButtons[i] != null && numberButtons[i].interactable;
            if (numberButtons[i] != null) numberButtons[i].interactable = false;
        }
        savedButtonInteractability[count] = deleteButton != null && deleteButton.interactable;
        savedButtonInteractability[count + 1] = joinButton != null && joinButton.interactable;
        if (deleteButton != null) deleteButton.interactable = false;
        if (joinButton != null) joinButton.interactable = false;
    }

    private void RestoreEntryButtons()
    {
        if (savedButtonInteractability == null)
            return;
        for (int i = 0; numberButtons != null && i < numberButtons.Length && i < savedButtonInteractability.Length; i++)
            if (numberButtons[i] != null) numberButtons[i].interactable = savedButtonInteractability[i];
        int count = numberButtons == null ? 0 : numberButtons.Length;
        if (deleteButton != null && count < savedButtonInteractability.Length)
            deleteButton.interactable = savedButtonInteractability[count];
        if (joinButton != null && count + 1 < savedButtonInteractability.Length)
            joinButton.interactable = savedButtonInteractability[count + 1];
        savedButtonInteractability = null;
        RefreshDisplay();
    }

    private void AddDigit(int digit)
    {
        if (code.Length >= CodeLength) return;
        code += digit.ToString();
        if (statusText != null) statusText.text = string.Empty;
        RefreshDisplay();
    }

    private void DeleteDigit()
    {
        if (code.Length == 0) return;
        code = code.Substring(0, code.Length - 1);
        if (statusText != null) statusText.text = string.Empty;
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (codeSlots != null)
            for (int i = 0; i < codeSlots.Length; i++)
                if (codeSlots[i] != null)
                    codeSlots[i].text = i < code.Length ? code[i].ToString() : string.Empty;

        if (joinButton != null)
        {
            bool hasCompleteCode = code.Length == CodeLength;
            joinButton.interactable = hasCompleteCode;
            if (joinButton.image != null)
                joinButton.image.color = hasCompleteCode
                    ? new Color32(207, 31, 42, 255)
                    : new Color32(211, 211, 211, 255);
        }
    }

    private void JoinSession()
    {
        if (isValidatingSession)
            return;

        if (code.Length != CodeLength || !IsNumeric(code))
        {
            if (statusText != null) statusText.text = "Ingresa un código válido de 6 dígitos.";
            return;
        }

        isValidatingSession = true;
        if (joinButton != null) joinButton.interactable = false;
        StartCoroutine(ValidateSessionAndContinue());
    }

    private IEnumerator ValidateSessionAndContinue()
    {
        if (statusText != null)
            statusText.text = "Buscando la sesión...";

        string baseUrl = SerenaServerConfig.BaseUrl;
        using var request = UnityWebRequest.Get($"{baseUrl}/api/sessions/{code}");
        request.timeout = 8;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            if (statusText != null)
                statusText.text = request.responseCode == 404
                    ? "Código inexistente o sesión vencida."
                    : "Sin conexión. Revisa la red.";
            if (joinButton != null) joinButton.interactable = true;
            isValidatingSession = false;
            yield break;
        }

        StudentSessionContext.SetSessionId(code);
        SceneManager.LoadScene("2_SampleScene");
    }

    private static bool IsNumeric(string value)
    {
        foreach (char character in value)
            if (character < '0' || character > '9') return false;
        return true;
    }
}

/// <summary>Applies consistent pressed, focused, and disabled feedback to student UI buttons.</summary>
public static class StudentButtonDepthStyle
{
    public static void Apply(Button button)
    {
        if (button == null)
            return;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
        colors.selectedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
        colors.disabledColor = new Color(0.62f, 0.62f, 0.62f, 0.62f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.transition = Selectable.Transition.ColorTint;
    }
}
