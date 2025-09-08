using Extensions;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class PanelComponent : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public void OpenPanel()
    {
        canvasGroup.DoShowGroup(0.5f);
    }

    public void ClosePanel()
    {
        canvasGroup.DoHideGroup(0.5f);
    }
}
