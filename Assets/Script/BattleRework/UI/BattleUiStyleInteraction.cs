using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[AddComponentMenu("")]
[DisallowMultipleComponent]
public sealed class BattleUiStyleInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
{
    private Button button;
    private readonly List<Graphic> graphics = new();
    private bool hovered, selected, pressed;
    private float scale = 1f, from = 1f, destination = 1f, started, duration;
    internal float Scale => BattleUiStyleController.IsStylish ? scale : 1f;
    internal bool Highlighted => button != null && button.IsInteractable() && (hovered || selected);
    internal bool Ready => button != null && button.IsInteractable();
    internal bool Primary => name.Contains("StartBattle") || name.Contains("Confirm") ||
        name.Contains("Done") || name.Contains("Resume");

    private void Awake() => button = GetComponent<Button>();

    internal static BattleUiStyleInteraction Bind(Graphic graphic)
    {
        Button owner = graphic.GetComponentInParent<Button>(true);
        if (owner == null) return null;
        BattleUiStyleInteraction interaction = owner.GetComponent<BattleUiStyleInteraction>();
        if (interaction == null) interaction = owner.gameObject.AddComponent<BattleUiStyleInteraction>();
        interaction.button = owner;
        if (!interaction.graphics.Contains(graphic)) interaction.graphics.Add(graphic);
        owner.onClick.RemoveListener(interaction.PlayConfirmed);
        owner.onClick.AddListener(interaction.PlayConfirmed);
        return interaction;
    }

    private void PlayConfirmed() => BattleUiStyleBurst.Play((RectTransform)transform);

    private void OnEnable()
    {
        ResetState();
        selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
    }

    private void OnDisable() => ResetState();

    private void ResetState()
    {
        hovered = selected = pressed = false;
        scale = from = destination = 1f;
        Dirty();
    }

    private void Dirty()
    {
        for (int i = graphics.Count - 1; i >= 0; i--)
        {
            if (graphics[i] == null) graphics.RemoveAt(i);
            else graphics[i].SetVerticesDirty();
        }
    }

    internal void RefreshState() => Dirty();

    private void Update()
    {
        if ((BattleUiStyleController.IsCmd || BattleUiStyleController.IsRetroJapanShop) && button != null && button.IsInteractable())
        {
            if (scale != 1f) { scale = 1f; Dirty(); }
            return;
        }
        if (!BattleUiStyleController.IsStylish || button == null || !button.IsInteractable())
        {
            if (scale != 1f || hovered || selected || pressed) ResetState();
            return;
        }
        bool visible = false;
        foreach (Graphic graphic in graphics)
            if (graphic != null && graphic.isActiveAndEnabled && !graphic.canvasRenderer.cull &&
                graphic.canvasRenderer.GetInheritedAlpha() > 0.01f) { visible = true; break; }
        if (!visible)
        {
            if (scale != 1f || hovered || selected || pressed) ResetState();
            return;
        }
        BattleUiStyleSettings style = BattleUiStyleController.Settings;
        float next = pressed ? style.stylishPressScale : Highlighted ? style.stylishHoverScale : 1f;
        if (destination != next)
        {
            from = scale;
            destination = next;
            started = Time.unscaledTime;
            duration = Mathf.Max(0.01f, pressed ? style.stylishPressDuration :
                next == 1f ? style.stylishCloseDuration : style.stylishHoverDuration);
        }
        float progress = Mathf.Clamp01((Time.unscaledTime - started) / Mathf.Max(0.01f, duration));
        float value = Mathf.Lerp(from, destination, 1f - Mathf.Pow(1f - progress, 3f));
        if (scale != value) { scale = value; Dirty(); }
    }

    public void OnPointerEnter(PointerEventData eventData) { hovered = true; Dirty(); }
    public void OnPointerExit(PointerEventData eventData) { hovered = pressed = false; Dirty(); }
    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) pressed = true;
    }
    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) pressed = false;
    }
    public void OnSelect(BaseEventData eventData) { selected = true; Dirty(); }
    public void OnDeselect(BaseEventData eventData) { selected = pressed = false; Dirty(); }
}
