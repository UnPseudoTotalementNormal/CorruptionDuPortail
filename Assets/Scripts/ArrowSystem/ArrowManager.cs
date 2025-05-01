using System.Collections.Generic;
using UnityEngine;

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
            if (instance == null)
            {
                instance = this;
            }
            else
            {
                Destroy(gameObject);
            }

            var _colliderTransform = arrowMouseCollider.transform;
            _colliderTransform.position = new Vector3(_colliderTransform.position.x, arrowPrefab.yPos, _colliderTransform.position.z);
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
            foreach (var _arrow in arrows)
            {
                Destroy(_arrow.gameObject);
            }
            arrows.Clear();
        }
        
        public void DestroyLastArrow()
        {
            if (arrows.Count <= 0)
            {
                return;
            }
            
            Destroy(arrows[^1].gameObject);
            arrows.RemoveAt(arrows.Count - 1);
            
            if (arrows.Count > 0)
            {
                GetLastArrow().SetIsInner(false);
            }
        }
    }
}