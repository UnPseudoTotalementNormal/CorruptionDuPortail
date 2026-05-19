using System;
using UnityEngine;

public class TransformFollower : MonoBehaviour
{
    public Transform transformToFollow;
    public Vector3 offset;

    private void Start()
    {
        FollowTransform();
    }

    private void Update()
    {
        FollowTransform();
    }

    private void FollowTransform()
    {
        if (!transformToFollow) return;
        transform.position = transformToFollow.position + offset;
    }
}
