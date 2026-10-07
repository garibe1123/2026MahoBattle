using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reward/Full PACK 슬롯의 선택, Hover, Drag Source, Drop Target 시각 표현을 전담합니다.
/// 데이터/입력 상태는 소유하지 않고 현재 상태를 받아 렌더링만 수행합니다.
/// </summary>
internal static class BattleInventorySelectionPresentation
{
    public static void EnsureFrames(
        RectTransform boardRoot,
        RectTransform[] slotRects,
        GameObject[] frames)
    {
        if (boardRoot == null ||
            slotRects == null ||
            frames == null)
        {
            return;
        }

        int count =
            Mathf.Min(
                BattleEquipmentSystem.MaxSlotCount,
                Mathf.Min(
                    slotRects.Length,
                    frames.Length));

        for (int i = 0; i < count; i++)
        {
            RectTransform slot =
                slotRects[i];

            if (slot == null)
            {
                slot =
                    FindChildRect(
                        boardRoot,
                        $"GridSlot_{i}");

                slotRects[i] =
                    slot;
            }

            if (slot == null ||
                frames[i] != null)
            {
                continue;
            }

            RectTransform frame =
                slot.Find("UnifiedSelectionFrame") as RectTransform;

            if (frame == null)
            {
                frame =
                    CreateRect(
                        slot,
                        "UnifiedSelectionFrame");

                Stretch(frame);
            }

            DisableRootGraphic(frame);
            EnsureStrokeEdges(frame);

            frames[i] =
                frame.gameObject;

            frame.gameObject.SetActive(
                false);
        }
    }

    public static void Apply(
        RectTransform boardRoot,
        RectTransform[] slotRects,
        GameObject[] frames,
        BattleEquipmentSystem equipmentSystem,
        BattleInventoryInteractionController interaction,
        bool inspectContext,
        bool rewardEdit,
        bool selectionSuppressed,
        int activeInspectSlot,
        Color selectedAccent,
        Color hoverAccent,
        Color pickedAccent,
        float selectedFillAlpha)
    {
        EnsureFrames(
            boardRoot,
            slotRects,
            frames);

        if (frames == null)
            return;

        float pulse01 =
            0.5f +
            0.5f *
            Mathf.Sin(
                Time.unscaledTime *
                6.2f);

        bool dragging =
            rewardEdit &&
            interaction != null &&
            !interaction.PadModeActive &&
            interaction.DraggingSlot >= 0;

        int dragSourceSlot =
            dragging
                ? interaction.DraggingSlot
                : -1;

        int dragTargetSlot =
            dragging
                ? interaction.HoveredSlot
                : -1;

        int count =
            Mathf.Min(
                BattleEquipmentSystem.MaxSlotCount,
                frames.Length);

        for (int i = 0; i < count; i++)
        {
            GameObject frame =
                frames[i];

            if (frame == null)
                continue;

            bool picked =
                rewardEdit &&
                interaction != null &&
                interaction.PadPickedSlot == i;

            bool padSelected =
                rewardEdit &&
                interaction != null &&
                interaction.PadModeActive &&
                interaction.PadSelectedSlot == i;

            bool mouseSelected =
                rewardEdit &&
                interaction != null &&
                !interaction.PadModeActive &&
                interaction.SelectedRewardSlot == i;

            bool hovered =
                rewardEdit &&
                interaction != null &&
                !interaction.PadModeActive &&
                interaction.HoveredSlot == i;

            bool hasItem =
                HasItem(
                    equipmentSystem,
                    i);

            bool previewSelected =
                inspectContext &&
                !selectionSuppressed &&
                i == activeInspectSlot &&
                hasItem;

            bool dragSource =
                dragging &&
                i == dragSourceSlot;

            bool dragTarget =
                dragging &&
                i == dragTargetSlot &&
                i != dragSourceSlot;

            bool targetUnlocked =
                equipmentSystem != null &&
                equipmentSystem.IsSlotUnlocked(i);

            bool visible =
                (hasItem &&
                 (picked ||
                  padSelected ||
                  mouseSelected ||
                  previewSelected)) ||
                dragSource ||
                dragTarget;

            if (frame.activeSelf != visible)
                frame.SetActive(visible);

            if (!visible)
                continue;

            Color color =
                dragTarget
                    ? !targetUnlocked
                        ? pickedAccent
                        : hasItem
                            ? selectedAccent
                            : hoverAccent
                    : dragSource
                        ? selectedAccent
                        : picked
                            ? pickedAccent
                            : mouseSelected || padSelected
                                ? selectedAccent
                                : hovered
                                    ? hoverAccent
                                    : selectedAccent;

            float thickness =
                dragTarget
                    ? Mathf.Lerp(
                        5.4f,
                        7.2f,
                        pulse01)
                    : dragSource
                        ? Mathf.Lerp(
                            4.6f,
                            6.0f,
                            pulse01)
                        : picked
                            ? Mathf.Lerp(
                                5.5f,
                                7f,
                                pulse01)
                            : mouseSelected || padSelected
                                ? Mathf.Lerp(
                                    5.2f,
                                    6.8f,
                                    pulse01)
                                : hovered
                                    ? 3f
                                    : Mathf.Lerp(
                                        3.8f,
                                        5f,
                                        pulse01);

            RectTransform frameRect =
                frame.GetComponent<RectTransform>();

            ApplyStrokeEdges(
                frameRect,
                color,
                thickness);

            ApplySelectionDecor(
                frameRect,
                color,
                mouseSelected ||
                padSelected ||
                picked ||
                dragSource,
                picked,
                pulse01,
                selectedFillAlpha);

            ApplyDragStateDecor(
                frameRect,
                dragSource,
                dragTarget,
                targetUnlocked,
                hasItem,
                pulse01,
                selectedAccent,
                hoverAccent,
                pickedAccent);
        }
    }

    private static void ApplySelectionDecor(
        RectTransform frame,
        Color color,
        bool lockedSelection,
        bool picked,
        float pulse01,
        float selectedFillAlpha)
    {
        if (frame == null)
            return;

        RectTransform fill =
            frame.Find("SelectionFill") as RectTransform;

        if (fill == null)
        {
            fill =
                CreateRect(
                    frame,
                    "SelectionFill");

            Stretch(fill);
            fill.SetAsFirstSibling();

            Image fillImage =
                fill.gameObject.AddComponent<Image>();

            fillImage.raycastTarget =
                false;
        }

        Image fillGraphic =
            fill.GetComponent<Image>();

        if (fillGraphic != null)
        {
            float alpha =
                lockedSelection
                    ? Mathf.Lerp(
                        selectedFillAlpha * 0.72f,
                        selectedFillAlpha,
                        pulse01)
                    : 0.035f;

            if (picked)
                alpha = Mathf.Max(alpha, 0.16f);

            fillGraphic.color =
                new Color(
                    color.r,
                    color.g,
                    color.b,
                    alpha);
        }

        RectTransform selectedBar =
            frame.Find("SelectedStateBar") as RectTransform;

        if (selectedBar == null)
        {
            selectedBar =
                CreateRect(
                    frame,
                    "SelectedStateBar");

            selectedBar.anchorMin =
                new Vector2(
                    0f,
                    0f);

            selectedBar.anchorMax =
                new Vector2(
                    1f,
                    0f);

            selectedBar.pivot =
                new Vector2(
                    0.5f,
                    0f);

            selectedBar.sizeDelta =
                new Vector2(
                    0f,
                    7f);

            selectedBar.anchoredPosition =
                Vector2.zero;

            Image barImage =
                selectedBar.gameObject.AddComponent<Image>();

            barImage.raycastTarget =
                false;
        }

        selectedBar.gameObject.SetActive(
            lockedSelection);

        Image selectedBarImage =
            selectedBar.GetComponent<Image>();

        if (selectedBarImage != null)
        {
            selectedBarImage.color =
                new Color(
                    color.r,
                    color.g,
                    color.b,
                    lockedSelection
                        ? Mathf.Lerp(
                            0.76f,
                            1f,
                            pulse01)
                        : 0f);
        }
    }

    private static void ApplyDragStateDecor(
        RectTransform frame,
        bool dragSource,
        bool dragTarget,
        bool targetUnlocked,
        bool targetOccupied,
        float pulse01,
        Color selectedAccent,
        Color hoverAccent,
        Color pickedAccent)
    {
        if (frame == null)
            return;

        RectTransform layer =
            frame.Find("DragStateFill") as RectTransform;

        if (layer == null)
        {
            layer =
                CreateRect(
                    frame,
                    "DragStateFill");

            Stretch(layer);
            layer.SetAsFirstSibling();

            Image image =
                layer.gameObject.AddComponent<Image>();

            image.raycastTarget =
                false;
        }

        layer.gameObject.SetActive(
            dragSource ||
            dragTarget);

        Image graphic =
            layer.GetComponent<Image>();

        if (graphic == null)
            return;

        if (dragSource)
        {
            graphic.color =
                new Color(
                    0f,
                    0f,
                    0f,
                    Mathf.Lerp(
                        0.30f,
                        0.42f,
                        pulse01));

            return;
        }

        Color color =
            !targetUnlocked
                ? pickedAccent
                : targetOccupied
                    ? selectedAccent
                    : hoverAccent;

        graphic.color =
            new Color(
                color.r,
                color.g,
                color.b,
                dragTarget
                    ? Mathf.Lerp(
                        0.07f,
                        0.14f,
                        pulse01)
                    : 0f);
    }

    private static bool HasItem(
        BattleEquipmentSystem equipmentSystem,
        int slotIndex)
    {
        return equipmentSystem != null &&
               slotIndex >= 0 &&
               slotIndex < equipmentSystem.Slots.Count &&
               equipmentSystem.IsSlotUnlocked(slotIndex) &&
               equipmentSystem.Slots[slotIndex] != null &&
               equipmentSystem.Slots[slotIndex].equipment != null;
    }

    private static void DisableRootGraphic(
        RectTransform frame)
    {
        Image image =
            frame != null
                ? frame.GetComponent<Image>()
                : null;

        if (image != null)
            image.enabled = false;

        Outline outline =
            frame != null
                ? frame.GetComponent<Outline>()
                : null;

        if (outline != null)
            outline.enabled = false;
    }

    private static void EnsureStrokeEdges(
        RectTransform frame)
    {
        if (frame == null)
            return;

        ConfigureStrokeEdge(
            frame,
            "Top",
            Color.white,
            4f,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0f, -4f),
            Vector2.zero);

        ConfigureStrokeEdge(
            frame,
            "Bottom",
            Color.white,
            4f,
            Vector2.zero,
            new Vector2(1f, 0f),
            Vector2.zero,
            new Vector2(0f, 4f));

        ConfigureStrokeEdge(
            frame,
            "Left",
            Color.white,
            4f,
            Vector2.zero,
            new Vector2(0f, 1f),
            Vector2.zero,
            new Vector2(4f, 0f));

        ConfigureStrokeEdge(
            frame,
            "Right",
            Color.white,
            4f,
            new Vector2(1f, 0f),
            Vector2.one,
            new Vector2(-4f, 0f),
            Vector2.zero);
    }

    private static void ApplyStrokeEdges(
        RectTransform frame,
        Color color,
        float thickness)
    {
        if (frame == null)
            return;

        thickness =
            Mathf.Max(
                1f,
                thickness);

        ConfigureStrokeEdge(
            frame,
            "Top",
            color,
            thickness,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0f, -thickness),
            Vector2.zero);

        ConfigureStrokeEdge(
            frame,
            "Bottom",
            color,
            thickness,
            Vector2.zero,
            new Vector2(1f, 0f),
            Vector2.zero,
            new Vector2(0f, thickness));

        ConfigureStrokeEdge(
            frame,
            "Left",
            color,
            thickness,
            Vector2.zero,
            new Vector2(0f, 1f),
            Vector2.zero,
            new Vector2(thickness, 0f));

        ConfigureStrokeEdge(
            frame,
            "Right",
            color,
            thickness,
            new Vector2(1f, 0f),
            Vector2.one,
            new Vector2(-thickness, 0f),
            Vector2.zero);
    }

    private static void ConfigureStrokeEdge(
        RectTransform root,
        string edgeName,
        Color color,
        float thickness,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        RectTransform edge =
            root.Find(edgeName) as RectTransform;

        if (edge == null)
        {
            GameObject edgeObject =
                new(edgeName);

            edgeObject.transform.SetParent(
                root,
                false);

            edge =
                edgeObject.AddComponent<RectTransform>();

            Image edgeImage =
                edgeObject.AddComponent<Image>();

            edgeImage.raycastTarget =
                false;
        }

        edge.anchorMin =
            anchorMin;

        edge.anchorMax =
            anchorMax;

        edge.offsetMin =
            offsetMin;

        edge.offsetMax =
            offsetMax;

        edge.localScale =
            Vector3.one;

        edge.localRotation =
            Quaternion.identity;

        Image image =
            edge.GetComponent<Image>();

        if (image != null)
        {
            image.enabled =
                true;

            image.color =
                color;

            image.raycastTarget =
                false;
        }
    }

    private static RectTransform FindChildRect(
        Transform parent,
        string objectName)
    {
        if (parent == null)
            return null;

        RectTransform[] all =
            parent.GetComponentsInChildren<RectTransform>(
                true);

        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null &&
                all[i].name == objectName)
            {
                return all[i];
            }
        }

        return null;
    }

    private static RectTransform CreateRect(
        Transform parent,
        string name)
    {
        GameObject go =
            new(name);

        go.transform.SetParent(
            parent,
            false);

        return go.AddComponent<RectTransform>();
    }

    private static void Stretch(
        RectTransform rect)
    {
        rect.anchorMin =
            Vector2.zero;

        rect.anchorMax =
            Vector2.one;

        rect.offsetMin =
            Vector2.zero;

        rect.offsetMax =
            Vector2.zero;

        rect.localScale =
            Vector3.one;

        rect.localRotation =
            Quaternion.identity;
    }
}
