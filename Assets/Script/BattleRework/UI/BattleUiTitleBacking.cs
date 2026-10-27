using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[AddComponentMenu("")]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleUiTitleBacking : MaskableGraphic
{
    private struct Piece
    {
        internal Vector2 a, b, c, d;
    }

    private readonly List<Piece> pieces = new();
    private readonly VertexHelper geometry = new();
    private Mesh backingMesh;
    private Color ink, rim, light;
    private float stroke, glow, line;
    private bool neon;

    internal void SetGeometry(TMP_TextInfo info, BattleUiStyleSettings settings, float extent, bool stylish, Color tint, float scale)
    {
        pieces.Clear();
        stroke = extent;
        neon = stylish;
        glow = stylish ? Mathf.Clamp(settings.inkGlowWidth, 8f, 14f) * scale : 0f;
        line = stylish ? Mathf.Clamp(settings.inkOutlineWidth, 3f, 5f) * scale : extent;
        ink = stylish ? settings.inkBackground : settings.comicInk;
        rim = stylish ? settings.inkBackground : settings.comicWhite;
        light = settings.inkCyan;
        ink.a *= tint.a;
        rim.a *= tint.a;
        light.a *= tint.a;
        color = Color.white;
        canvasRenderer.SetColor(tint);
        int previousLine = -1;
        Piece previous = default;
        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo character = info.characterInfo[i];
            if (!character.isVisible) { previousLine = -1; continue; }
            Vector3[] v = info.meshInfo[character.materialReferenceIndex].vertices;
            int index = character.vertexIndex;
            Piece piece = new() { a = v[index], b = v[index + 1], c = v[index + 2], d = v[index + 3] };
            if (previousLine == character.lineNumber)
            {
                Vector2 from = (previous.a + previous.c) * 0.5f;
                Vector2 to = (piece.a + piece.c) * 0.5f;
                float height = Mathf.Min(Vector2.Distance(previous.a, previous.b), Vector2.Distance(piece.a, piece.b));
                if (to.x > from.x && Vector2.Distance(from, to) <= height * 1.25f)
                {
                    Vector2 up = new Vector2(from.y - to.y, to.x - from.x).normalized * stroke * 0.15f;
                    pieces.Add(new Piece { a = from - up, b = from + up, c = to + up, d = to - up });
                }
            }
            previous = piece;
            previousLine = character.lineNumber;
        }
        if (backingMesh == null) backingMesh = new Mesh { name = "Title Backing", hideFlags = HideFlags.HideAndDontSave };
        OnPopulateMesh(geometry);
        geometry.FillMesh(backingMesh);
        canvasRenderer.SetMesh(backingMesh);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (neon)
        {
            Color halo = light;
            halo.a *= 0.08f;
            DrawLayer(vh, stroke + glow, Vector2.zero, halo);
            halo.a *= 2f;
            DrawLayer(vh, stroke + glow * 0.5f, Vector2.zero, halo);
            DrawLayer(vh, stroke + line, Vector2.zero, light);
            DrawLayer(vh, stroke, Vector2.zero, ink);
        }
        else
        {
            DrawLayer(vh, stroke * 1.6f, new Vector2(stroke * 0.35f, -stroke * 0.35f), ink);
            DrawLayer(vh, stroke, Vector2.zero, rim);
        }
    }

    private void DrawLayer(VertexHelper vh, float expand, Vector2 offset, Color tint)
    {
        foreach (Piece piece in pieces)
        {
            Vector2 across = ((piece.c + piece.d) - (piece.a + piece.b)).normalized * expand;
            Vector2 up = ((piece.b + piece.c) - (piece.a + piece.d)).normalized * expand;
            int first = vh.currentVertCount;
            vh.AddVert(piece.a - across - up + offset, tint, Vector2.zero);
            vh.AddVert(piece.b - across + up + offset, tint, Vector2.zero);
            vh.AddVert(piece.c + across + up + offset, tint, Vector2.zero);
            vh.AddVert(piece.d + across - up + offset, tint, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first, first + 2, first + 3);
        }
    }

    protected override void OnDestroy()
    {
        geometry.Dispose();
        if (backingMesh != null) BattleUiCmdFrame.Release(backingMesh);
        base.OnDestroy();
    }
}
