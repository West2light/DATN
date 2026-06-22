using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UiFollowTank : MonoBehaviour
{
    public Transform objectToFollow;
    RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }


    private void LateUpdate()
    {
        if (objectToFollow != null)
        {
            rectTransform.anchoredPosition = objectToFollow.localPosition;
            // Keep world-space UI horizontal even when a multiplayer ghost rotates
            // its root transform. This also preserves the existing single-player look.
            rectTransform.rotation = Quaternion.identity;
        }
    }
}
