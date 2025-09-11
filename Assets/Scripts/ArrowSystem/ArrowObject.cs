#region

using UnityEngine;

#endregion

public class ArrowObject : MonoBehaviour
{
    public SpriteRenderer arrowSpriteRenderer;
    
    [SerializeField] private Sprite outerArrowSprite;
    [SerializeField] private Sprite innerArrowSprite;
    
    private Vector3 pointA;
    private Vector3 pointB;

    public float yPos = -14;
    
    public void SetIsInner(bool _isInner)
    {
        arrowSpriteRenderer.sprite = _isInner ? innerArrowSprite : outerArrowSprite;
    }
    
    public void SetPointA(Vector3 _pointA)
    {
        _pointA.y = yPos;
        pointA = _pointA;
        UpdatePosition();
    }
    
    public void SetPointB(Vector3 _pointB)
    {
        _pointB.y = yPos;
        pointB = _pointB;
        UpdatePosition();
    }

    private void UpdatePosition()
    {
        transform.position = (pointA + pointB) / 2;
        
        Vector3 _direction = new Vector3(pointB.x - pointA.x, 0, pointB.z - pointA.z);
        float _angle = Mathf.Atan2(_direction.x, _direction.z) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(new Vector3(0, _angle, 0));
        
        float _distance = Vector3.Distance(pointA, pointB);
        arrowSpriteRenderer.size = new Vector2(_distance, arrowSpriteRenderer.size.y);
    }
}
