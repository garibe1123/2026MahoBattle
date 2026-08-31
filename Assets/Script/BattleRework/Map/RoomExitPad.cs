using System;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class RoomExitPad : MonoBehaviour
{
    private Transform player;
    private Action onActivated;
    private bool armed;

    public void Arm(Transform playerTarget, Action callback)
    {
        player = playerTarget;
        onActivated = callback;
        armed = true;

        Collider2D col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!armed || player == null) return;
        if (other.transform != player && !other.transform.IsChildOf(player)) return;

        armed = false;
        Action callback = onActivated;
        onActivated = null;
        callback?.Invoke();
    }
}
