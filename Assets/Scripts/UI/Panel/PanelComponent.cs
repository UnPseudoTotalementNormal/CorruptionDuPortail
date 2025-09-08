using Extensions;
using UI.Panel;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class PanelComponent : MonoBehaviour, IPanelComponent
{
    [SerializeField] private CanvasGroup canvasGroup;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public virtual bool TryOpenPanel()
    {
        OpenPanel();
        return true;
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
