using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleRuleGlyph : MaskableGraphic
{
    private enum Symbol { Sword, Heal, Coin, Boot, Shield, Hammer, Rush, NoHeal, Glass, Blood, Bomb, Frenzy, NoHealShow }
    private Symbol symbol;
    private Vector2 center;
    private float size;
    private Color ink, halo;

    public void Configure(BattleRuleDefinition rule, Color tint)
    {
        symbol = rule.id switch
        {
            "power_play" => Symbol.Sword,
            "second_wind" => Symbol.Heal,
            "bounty_night" => Symbol.Coin,
            "light_feet" => Symbol.Boot,
            "armored_cast" => Symbol.Shield,
            "hard_hitters" => Symbol.Hammer,
            "rushing_mob" => Symbol.Rush,
            "no_rest" => Symbol.NoHeal,
            "glass_cannon" => Symbol.Glass,
            "blood_money" => Symbol.Blood,
            "short_fuse" => Symbol.Bomb,
            "frenzy" => Symbol.Frenzy,
            "no_heal_show" => Symbol.NoHealShow,
            _ => rule.healingMultiplier != 1f || rule.preCombatHealFraction > 0f ? Symbol.Heal :
                rule.killPointMultiplier != 1f ? Symbol.Coin :
                rule.playerMoveSpeedMultiplier != 1f || rule.enemyMoveSpeedMultiplier != 1f ? Symbol.Boot :
                rule.enemyHpMultiplier != 1f ? Symbol.Shield : Symbol.Sword
        };
        color = tint;
        raycastTarget = false;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = rectTransform.rect;
        size = Mathf.Min(rect.width, rect.height) * 0.46f;
        if (size <= 0f) return;
        center = rect.center;
        ink = color;
        halo = new Color(0.035f, 0.04f, 0.08f);
        halo.a = ink.a;
        switch (symbol)
        {
            case Symbol.Sword:
            case Symbol.Glass:
                Line(vh, new(-0.52f, -0.62f), new(0.57f, 0.61f), 0.21f);
                Triangle(vh, new(0.33f, 0.64f), new(0.75f, 0.82f), new(0.65f, 0.40f), ink);
                Line(vh, new(-0.55f, -0.13f), new(0.02f, -0.62f), 0.14f);
                if (symbol == Symbol.Glass)
                {
                    Line(vh, new(0.45f, -0.10f), new(0.24f, -0.40f), 0.09f);
                    Line(vh, new(0.24f, -0.40f), new(0.54f, -0.52f), 0.09f);
                    Line(vh, new(0.54f, -0.52f), new(0.31f, -0.82f), 0.09f);
                }
                break;
            case Symbol.Heal:
            case Symbol.NoHeal:
            case Symbol.NoHealShow:
                Ring(vh, Vector2.zero, 0.74f, 0.08f, ink);
                Line(vh, new(-0.40f, 0f), new(0.40f, 0f), 0.23f);
                Line(vh, new(0f, -0.40f), new(0f, 0.40f), 0.23f);
                if (symbol != Symbol.Heal) Line(vh, new(-0.69f, -0.69f), new(0.69f, 0.69f), 0.13f);
                if (symbol == Symbol.NoHealShow) Coin(vh, new(0.58f, -0.53f), 0.32f);
                break;
            case Symbol.Coin:
            case Symbol.Blood:
                Coin(vh, new(-0.10f, 0.08f), 0.72f);
                if (symbol == Symbol.Blood)
                {
                    Triangle(vh, new(0.47f, -0.05f), new(0.19f, -0.60f), new(0.75f, -0.60f), ink);
                    Disc(vh, new(0.47f, -0.60f), 0.28f, ink);
                }
                break;
            case Symbol.Boot:
                Line(vh, new(-0.15f, 0.72f), new(-0.15f, -0.40f), 0.44f);
                Line(vh, new(-0.17f, -0.43f), new(0.59f, -0.43f), 0.35f);
                Line(vh, new(-0.55f, -0.72f), new(0.73f, -0.72f), 0.10f);
                Line(vh, new(-0.79f, 0.37f), new(-0.49f, 0.37f), 0.09f);
                Line(vh, new(-0.87f, 0.10f), new(-0.49f, 0.10f), 0.09f);
                break;
            case Symbol.Shield:
                Triangle(vh, new(-0.65f, 0.54f), new(0.65f, 0.54f), new(0f, -0.82f), ink);
                Line(vh, new(-0.65f, 0.54f), new(0f, 0.80f), 0.14f);
                Line(vh, new(0f, 0.80f), new(0.65f, 0.54f), 0.14f);
                Line(vh, new(0f, 0.40f), new(0f, -0.30f), 0.10f, halo);
                break;
            case Symbol.Hammer:
                Line(vh, new(-0.48f, -0.73f), new(0.31f, 0.42f), 0.20f);
                Line(vh, new(-0.24f, 0.73f), new(0.70f, 0.12f), 0.43f);
                break;
            case Symbol.Rush:
            case Symbol.Frenzy:
                Arrow(vh, -0.34f, false);
                Arrow(vh, 0.34f, symbol == Symbol.Frenzy);
                break;
            case Symbol.Bomb:
                Disc(vh, new(-0.13f, -0.18f), 0.63f, ink);
                Ring(vh, new(-0.13f, -0.18f), 0.67f, 0.06f, halo);
                Line(vh, new(0.15f, 0.33f), new(0.36f, 0.70f), 0.14f);
                Line(vh, new(0.36f, 0.70f), new(0.65f, 0.64f), 0.09f);
                Line(vh, new(0.66f, 0.82f), new(0.66f, 0.43f), 0.08f);
                Line(vh, new(0.46f, 0.64f), new(0.86f, 0.64f), 0.08f);
                break;
        }
    }

    private void Arrow(VertexHelper vh, float y, bool reverse)
    {
        float sign = reverse ? -1f : 1f;
        Line(vh, new(-0.74f * sign, y), new(0.59f * sign, y), 0.13f);
        Line(vh, new(0.28f * sign, y + 0.26f), new(0.64f * sign, y), 0.13f);
        Line(vh, new(0.28f * sign, y - 0.26f), new(0.64f * sign, y), 0.13f);
    }

    private void Coin(VertexHelper vh, Vector2 position, float radius)
    {
        Ring(vh, position, radius + 0.04f, 0.19f, halo);
        Ring(vh, position, radius, 0.10f, ink);
        Line(vh, position + new Vector2(0f, -radius * 0.55f), position + new Vector2(0f, radius * 0.55f), 0.12f);
        Line(vh, position + new Vector2(-radius * 0.28f, radius * 0.35f), position + new Vector2(radius * 0.28f, radius * 0.35f), 0.10f);
        Line(vh, position + new Vector2(-radius * 0.28f, -radius * 0.35f), position + new Vector2(radius * 0.28f, -radius * 0.35f), 0.10f);
    }

    private void Line(VertexHelper vh, Vector2 a, Vector2 b, float width)
    {
        Line(vh, a, b, width + 0.07f, halo);
        Line(vh, a, b, width, ink);
    }

    private void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 delta = b - a;
        Vector2 normal = new Vector2(-delta.y, delta.x).normalized * width * 0.5f;
        int first = vh.currentVertCount;
        Vertex(vh, a - normal, tint);
        Vertex(vh, a + normal, tint);
        Vertex(vh, b + normal, tint);
        Vertex(vh, b - normal, tint);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }

    private void Disc(VertexHelper vh, Vector2 position, float radius, Color tint)
    {
        int first = vh.currentVertCount;
        Vertex(vh, position, tint);
        for (int i = 0; i < 20; i++)
        {
            float angle = i * Mathf.PI * 0.1f;
            Vertex(vh, position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint);
        }
        for (int i = 0; i < 20; i++) vh.AddTriangle(first, first + 1 + i, first + 1 + (i + 1) % 20);
    }

    private void Ring(VertexHelper vh, Vector2 position, float radius, float width, Color tint)
    {
        int first = vh.currentVertCount;
        for (int i = 0; i < 20; i++)
        {
            float angle = i * Mathf.PI * 0.1f;
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vertex(vh, position + direction * radius, tint);
            Vertex(vh, position + direction * (radius - width), tint);
        }
        for (int i = 0; i < 20; i++)
        {
            int a = first + i * 2, b = first + (i + 1) % 20 * 2;
            vh.AddTriangle(a, b, b + 1);
            vh.AddTriangle(a, b + 1, a + 1);
        }
    }

    private void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int first = vh.currentVertCount;
        Vertex(vh, a, tint);
        Vertex(vh, b, tint);
        Vertex(vh, c, tint);
        vh.AddTriangle(first, first + 1, first + 2);
    }

    private void Vertex(VertexHelper vh, Vector2 point, Color tint) =>
        vh.AddVert((Vector3)(center + point * size), tint, Vector2.zero);
}
