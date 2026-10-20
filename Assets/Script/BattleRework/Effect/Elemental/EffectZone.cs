using System;
using System.Collections.Generic;
using UnityEngine;

public class EffectZone : MonoBehaviour
{
    public EffectFaction faction;
    public ElementDefinitionSO[] elements = Array.Empty<ElementDefinitionSO>();
    [Min(0.01f)] public float tickInterval = 0.2f;
    private readonly Dictionary<Collider2D, IElementReceiver> occupants = new();
    private float timer;
    public event Action<Collider2D, IElementReceiver> Entered;
    public event Action<Collider2D, IElementReceiver> Stayed;
    public event Action<Collider2D, IElementReceiver> Exited;
    public event Action<float> Ticked;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (occupants.ContainsKey(other)) return;
        IElementReceiver receiver = other.GetComponentInParent<IElementReceiver>();
        occupants.Add(other, receiver);
        OnEnter(other, receiver);
        Entered?.Invoke(other, receiver);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!occupants.TryGetValue(other, out IElementReceiver receiver)) return;
        OnStay(other, receiver);
        Stayed?.Invoke(other, receiver);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!occupants.TryGetValue(other, out IElementReceiver receiver)) return;
        occupants.Remove(other);
        OnExit(other, receiver);
        Exited?.Invoke(other, receiver);
    }

    private void Update()
    {
        timer += EffectNumbers.NonNegative(Time.deltaTime);
        float interval = EffectNumbers.Positive(tickInterval, 0.2f);
        if (timer < interval) return;
        float elapsed = timer;
        timer %= interval;
        OnTick(elapsed);
        Ticked?.Invoke(elapsed);
    }

    protected virtual void OnEnter(Collider2D other, IElementReceiver receiver) { }
    protected virtual void OnStay(Collider2D other, IElementReceiver receiver) { }
    protected virtual void OnExit(Collider2D other, IElementReceiver receiver) { }
    protected virtual void OnTick(float elapsed) { }

    private void OnDisable()
    {
        occupants.Clear();
        timer = 0f;
        Entered = null;
        Stayed = null;
        Exited = null;
        Ticked = null;
    }
}
