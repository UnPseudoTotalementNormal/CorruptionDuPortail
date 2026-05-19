#region

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

#endregion

namespace Board.CardComponents
{
    public interface ICardAnimation
    {
        void Initialize(CardVisualComponents _visualComponents);
        void Update();
        
        bool CanUnZoom();
        void OnHover(Canvas _cardCanvas, Action _onZoomStarted = null);
        void OnUnHover(Canvas _cardCanvas, Action _onZoomEnded = null);
        void OnClick();
        UniTask FlipToBack(bool _instant = false, Action _onFlipStart = null);
        UniTask FlipToFront(bool _instant = false, Action _onFlipStart = null);
        
        bool isCardZoomed { get; }
        Vector3 velocity { get; }
        Vector3 angularVelocity { get; }
        bool isCardMoving { get; }
        
        event Action onCardStartMoving;
        event Action onCardStopMoving;
    }
}

