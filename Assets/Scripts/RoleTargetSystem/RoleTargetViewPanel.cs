using Extensions;
using UnityEngine;

namespace RoleTarget
{
    public class RoleTargetViewPanel : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        
        private void OpenPanel()
        {
            canvasGroup.DoShowGroup(0.5f);
        }

        public void ClosePanel()
        {
            canvasGroup.DoHideGroup(0.5f);
        }
    }
}