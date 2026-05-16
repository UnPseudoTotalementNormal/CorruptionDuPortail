#region

using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

#endregion

namespace ArrowSystem
{
    public class ArrowManager : MonoBehaviour
    {
        public static ArrowManager instance;
        public ArrowObject arrowPrefab;

        public Transform arrowMouseCollider;
        
        public List<ArrowObject> arrows = new List<ArrowObject>();
        
        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;

            var _colliderTransform = arrowMouseCollider.transform;
            _colliderTransform.position = new Vector3(_colliderTransform.position.x, arrowPrefab.yPos, _colliderTransform.position.z);
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }
        
        public ArrowObject GetLastArrow()
        {
            if (arrows.Count > 0)
            {
                return arrows[^1];
            }
            return null;
        }
        
        public ArrowObject StartNewArrow(Vector3 _pointA, Vector3 _pointB)
        {
            DestroyAllArrows();
            return CreateNewArrow(_pointA, _pointB);
        }
        
        public ArrowObject CreateNewArrow(Vector3 _pointA, Vector3 _pointB)
        {
            if (arrows.Count > 0)
            {
                GetLastArrow().SetIsInner(true);
            }
            ArrowObject _newArrow = Instantiate(arrowPrefab, transform);
            _newArrow.SetPointA(_pointA);
            _newArrow.SetPointB(_pointB);
            _newArrow.SetIsInner(false);
            arrows.Add(_newArrow);
            return _newArrow;
        }
        
        public void DestroyAllArrows()
        {
            foreach (var _arrow in arrows.ToList())
            {
                DestroyArrow(_arrow);
            }
            arrows.Clear();
        }
        
        public void DestroyLastArrow()
        {
            if (arrows.Count <= 0)
            {
                return;
            }
            
            DestroyArrow(arrows[^1]);
            
            if (arrows.Count > 0)
            {
                GetLastArrow().SetIsInner(false);
            }
        }

        private void DestroyArrow(ArrowObject _arrow)
        {
            if (arrows.Contains(_arrow))
            {
                arrows.Remove(_arrow);
                _arrow.transform.DOScaleX(0, 0.25f).SetEase(Ease.OutQuint).OnComplete(() =>
                {
                    Destroy(_arrow);
                });
            }
        }
    }
}