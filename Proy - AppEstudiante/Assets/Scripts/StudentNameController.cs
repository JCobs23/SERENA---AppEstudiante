using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

#if UNITY_EDITOR
using UnityEditor; // Necesario para obtener la ruta de la carpeta desde el Inspector
#endif

public class StudentNameController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_InputField inputFieldName;
    [SerializeField] private Button continueButton;

    [Header("Panels Navigation")]
    [SerializeField] private GameObject currentPanel; // PANEL_Name
    [SerializeField] private GameObject nextPanel;    // Siguiente panel

    [Header("Folder Configuration")]
    [Tooltip("Arrastra aquí la carpeta del proyecto donde se guardará el JSON")]
    [SerializeField] private Object targetFolder; // Permite arrastrar la carpeta desde la ventana Project

    private string studentName = "";

    private void Start()
    {
        if (continueButton != null)
        {
            continueButton.interactable = false;
            continueButton.onClick.AddListener(OnContinuePressed);
        }

        if (inputFieldName != null)
        {
            inputFieldName.onValueChanged.AddListener(ValidateInput);
        }
    }

    private void ValidateInput(string input)
    {
        string trimmedInput = input.Trim();

        bool hasMoreThanSixChars = input.Length > 6;
        bool hasSpace = input.Contains(" ");

        if (hasMoreThanSixChars && hasSpace)
        {
            continueButton.interactable = true;
            studentName = trimmedInput;
        }
        else
        {
            continueButton.interactable = false;
        }
    }

    private void OnContinuePressed()
    {
        SaveStudentData(studentName);
    }

    private void SaveStudentData(string name)
    {
        string folderPath = GetFolderPath();

        if (string.IsNullOrEmpty(folderPath))
        {
            Debug.LogError("Error: No has asignado ninguna carpeta en el Inspector.");
            return;
        }

        StudentData data = new StudentData();
        data.studentName = name;

        string json = JsonUtility.ToJson(data, true);
        string fullFilePath = Path.Combine(folderPath, "student_data.json");

        // Crea el directorio si no existe físicamente
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        File.WriteAllText(fullFilePath, json);
        Debug.Log("<color=green>¡JSON guardado con éxito en!</color>: " + fullFilePath);

#if UNITY_EDITOR
        // Forzar a Unity a actualizar la ventana Project para que el JSON aparezca al instante
        AssetDatabase.Refresh();
#endif

        // Transición de paneles
        if (currentPanel != null) currentPanel.SetActive(false);
        if (nextPanel != null) nextPanel.SetActive(true);
    }

    /// <summary>
    /// Obtiene la ruta física del sistema basada en la carpeta arrastrada en el Inspector
    /// </summary>
    private string GetFolderPath()
    {
#if UNITY_EDITOR
        if (targetFolder != null)
        {
            // Obtiene la ruta relativa dentro de Assets (ej. "Assets/MisDatos")
            string relativePath = AssetDatabase.GetAssetPath(targetFolder);

            // Verifica si lo que se asignó es efectivamente una carpeta
            if (Directory.Exists(relativePath))
            {
                // Convierte la ruta relativa a una ruta absoluta completa de tu disco duro
                return Path.GetFullPath(relativePath);
            }
            else
            {
                Debug.LogWarning("El objeto asignado en 'Target Folder' no es una carpeta.");
            }
        }
#endif
        // Ruta por defecto en caso de no asignar nada en el Inspector o ejecutar fuera del editor
        return Application.dataPath;
    }
}

[System.Serializable]
public class StudentData
{
    public string studentName;
}