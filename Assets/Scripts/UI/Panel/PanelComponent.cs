using Extensions;
using UI.Panel;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class PanelComponent : MonoBehaviour, IPanelComponent
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float animationDuration = 0.5f;
    public bool isPanelOpen { get; private set; }

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }
    
    public void SwitchPanelOpen()
    {
        if (isPanelOpen)
        {
            ClosePanel();
        }
        else
        {
            TryOpenPanel();
        }
    }

    public virtual void TryOpenPanel()
    {
        OpenPanel();
        return;
    }

    public void OpenPanel()
    {
        isPanelOpen = true;
        canvasGroup.DoShowGroup(animationDuration);
    }

    public void ClosePanel()
    {
        isPanelOpen = false;
        canvasGroup.DoHideGroup(animationDuration);
    }
}
