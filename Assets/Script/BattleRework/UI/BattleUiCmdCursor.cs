using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

internal sealed class BattleUiCmdCursor : IDisposable
{
    private static readonly HashSet<BattleUiCmdCursor> cursors = new();
    private TextMeshProUGUI source, cursor;
    private bool wasTyping, wasVisible;
    private float completedAt;

    internal BattleUiCmdCursor() => cursors.Add(this);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => cursors.Clear();

    internal static void HideAll()
    {
        foreach (BattleUiCmdCursor value in cursors) value.Hide();
    }

    internal void Tick(TextMeshProUGUI rendered, bool visible, bool typing)
    {
        cursors.Add(this);
        if (!BattleUiStyleController.IsCmd || !visible || rendered == null || rendered.font == null || !rendered.isActiveAndEnabled)
        {
            Hide();
            return;
        }
        if (source != rendered)
        {
            Unsubscribe();
            source = rendered;
            source.OnPreRenderText += Position;
            if (cursor != null) BattleUiCmdFrame.Release(cursor.gameObject);
            cursor = BattleUiCmdFrame.CreateText(source.rectTransform, "CmdCursor");
            cursor.text = "_";
            cursor.alignment = TextAlignmentOptions.BaselineLeft;
            cursor.overflowMode = TextOverflowModes.Overflow;
            cursor.rectTransform.anchorMin = cursor.rectTransform.anchorMax = source.rectTransform.pivot;
            cursor.rectTransform.pivot = new Vector2(0f, 0.5f);
            wasVisible = false;
        }
        if (!wasVisible || (wasTyping && !typing)) completedAt = Time.unscaledTime;
        wasVisible = true;
        wasTyping = typing;
        BattleUiStyleSettings settings = BattleUiStyleController.Settings;
        float interval = Mathf.Max(0.05f, settings.cmdCursorBlinkSeconds);
        bool show = typing || Mathf.FloorToInt((Time.unscaledTime - completedAt) / interval) % 2 == 0;
        cursor.gameObject.SetActive(show);
        if (!show) return;
        cursor.font = source.font;
        cursor.fontSharedMaterial = source.font.material;
        cursor.fontSize = source.fontSize;
        cursor.color = settings.outerFrame;
        cursor.maskable = source.maskable;
        if (source.havePropertiesChanged) source.ForceMeshUpdate();
        Position(source.textInfo);
    }

    private void Position(TMP_TextInfo info)
    {
        if (cursor == null || !cursor.gameObject.activeSelf || source == null || source.font == null) return;
        Vector2 point;
        int count = Mathf.Min(info.characterCount, source.maxVisibleCharacters);
        if (count > 0)
        {
            TMP_CharacterInfo last = info.characterInfo[count - 1];
            point = new Vector2(last.xAdvance, last.baseLine);
            if (last.character == '\n' && last.lineNumber + 1 < info.lineCount)
                point = new Vector2(source.rectTransform.rect.xMin + source.margin.x,
                    info.lineInfo[last.lineNumber + 1].baseline);
        }
        else
        {
            float scale = source.fontSize / Mathf.Max(1f, source.font.faceInfo.pointSize) * source.font.faceInfo.scale;
            Rect rect = source.rectTransform.rect;
            point = new Vector2(rect.xMin + source.margin.x,
                rect.yMax - source.margin.y - source.font.faceInfo.ascentLine * scale);
        }
        cursor.rectTransform.sizeDelta = new Vector2(source.fontSize * 2f, source.fontSize * 2f);
        cursor.rectTransform.anchoredPosition = point;
    }

    internal void Hide()
    {
        wasVisible = wasTyping = false;
        if (cursor != null && cursor.gameObject.activeSelf) cursor.gameObject.SetActive(false);
    }

    private void Unsubscribe()
    {
        if (source != null) source.OnPreRenderText -= Position;
    }

    public void Dispose()
    {
        cursors.Remove(this);
        Unsubscribe();
        if (cursor != null) BattleUiCmdFrame.Release(cursor.gameObject);
        source = cursor = null;
    }
}
