using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class StudentRowView : MonoBehaviour
{
    [SerializeField] private RawImage status;
    [SerializeField] private TMP_Text studentName;

    public void Configure(string name, bool ready, Texture readyTexture, Texture notReadyTexture, Color readyColor, Color notReadyColor)
    {
        if (studentName != null)
            studentName.text = name;

        if (status != null)
        {
            status.texture = ready ? readyTexture : notReadyTexture;
            status.color = ready ? readyColor : notReadyColor;
        }
    }
}
