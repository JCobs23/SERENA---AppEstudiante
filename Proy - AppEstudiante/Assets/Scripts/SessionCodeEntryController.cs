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
    [SerializeField] private string serverBaseUrl = "http://127.0.0.1:5080";

    private string code = string.Empty;
    private bool isValidatingSession;

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
        RefreshDisplay();
    }

    private void OnDestroy()
    {
        if (numberButtons != null)
            foreach (Button button in numberButtons)
                if (button != null) button.onClick.RemoveAllListeners();

        if (deleteButton != null) deleteButton.onClick.RemoveListener(DeleteDigit);
        if (joinButton != null) joinButton.onClick.RemoveListener(JoinSession);
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

        string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
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
