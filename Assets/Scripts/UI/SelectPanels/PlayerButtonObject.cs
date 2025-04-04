using System;
using DG.Tweening;
using Network;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI.SelectPanels
{
    public class PlayerButtonObject : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public ulong playerId;
        
        private Color baseColor;
        [SerializeField] private Color hoverColor;

        private Image panelImage;
        
        public event Action<ulong> onPlayerButtonClicked;

        private void Awake()
        {
            panelImage = GetComponent<Image>();
            baseColor = panelImage.color;
        }

        private void Start()
        {
            foreach (var _playerInfo in LobbyPlayerInfoHolder.instance.playerInfos)
            {
                if (_playerInfo.playerClientId != playerId)
                {
                    continue;
                }
                
                GetComponentInChildren<TMP_Text>().text = _playerInfo.playerName.ToString();
                break;
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            transform.DOKill(true);
            transform.DOPunchScale(Vector3.one * 0.15f, 0.2f, 1, 0.2f);
            onPlayerButtonClicked?.Invoke(playerId);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            panelImage.DOColor(hoverColor, 0.2f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            panelImage.DOColor(baseColor, 0.2f);
        }
    }
}