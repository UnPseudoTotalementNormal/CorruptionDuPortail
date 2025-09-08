using Extensions;
using UI.Panel;
using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class PanelComponent : MonoBehaviour, IPanelComponent
{
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float animationDuration = 0.5f;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    public virtual void TryOpenPanel()
    {
        OpenPanel();
        return;
    }

    public void OpenPanel()
    {
        canvasGroup.DoShowGroup(animationDuration);
    }

    public void ClosePanel()
    {
        canvasGroup.DoHideGroup(animationDuration);
    }
}
