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

    private void Start()
    {
        if (numberButtons != null)
        {
            for (int i = 0; i < numberButtons.Length; i++)
            {
                int digit = i == 9 ? 0 : i + 1;
                if (numberButtons[i] != null)
                    numberButtons[i].onClick.AddListener(() => AddDigit(digit));
            }
        }

        if (deleteButton != null) deleteButton.onClick.AddListener(DeleteDigit);
        if (joinButton != null) joinButton.onClick.AddListener(JoinSession);
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
        if (code.Length != CodeLength || !IsNumeric(code))
        {
            if (statusText != null) statusText.text = "Ingresa un código válido de 6 dígitos.";
            return;
        }

        if (joinButton != null) joinButton.interactable = false;
        StartCoroutine(ValidateSessionAndContinue());
    }

    private IEnumerator ValidateSessionAndContinue()
    {
        string baseUrl = PlayerPrefs.GetString("SerenaServerUrl", serverBaseUrl).TrimEnd('/');
        using var request = UnityWebRequest.Get($"{baseUrl}/api/sessions/{code}");
        request.timeout = 8;
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            if (statusText != null)
                statusText.text = request.responseCode == 404
                    ? "El código no existe o la sesión ya expiró."
                    : "No se pudo conectar con Serena. Revisa la red y la dirección del servidor.";
            if (joinButton != null) joinButton.interactable = true;
            yield break;
        }

        StudentSessionContext.SetSessionId(code);
        SceneManager.LoadScene("SampleScene");
    }

    private static bool IsNumeric(string value)
    {
        foreach (char character in value)
            if (character < '0' || character > '9') return false;
        return true;
    }
}
