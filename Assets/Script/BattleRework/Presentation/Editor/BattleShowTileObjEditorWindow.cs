#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// BattleShowSetLayoutSO 전용 2D 배치 에디터.
/// (0,0)은 Persistent 4x4 Base의 좌하단 타일 중심입니다.
/// 회색 가이드는 Base, Screen Carrier, Presenter Carrier의 최종 도킹 영역입니다.
/// </summary>
public sealed class BattleShowTileObjEditorWindow : EditorWindow
{
    private const float BaseCellPixels = 46f;
    private const float CanvasHeight = 520f;
    private const int GridMinX = -8;
    private const int GridMaxX = 18;
    private const int GridMinY = -8;
    private const int GridMaxY = 14;

    private BattleShowSetLayoutSO layout;
    private SerializedObject serializedLayout;
    private Sprite brushSprite;
    private int selectedIndex = -1;
    private float zoom = 1f;
    private Vector2 pan;
    private bool replaceCellOnPaint;
    private bool showPresenterSection = true;
    private bool showFloorSection = true;
    private bool showPaletteSection = true;
    private Vector2 scroll;

    [MenuItem("Tools/Battle/Show/Tile Obj Editor")]
    public static void Open()
    {
        BattleShowTileObjEditorWindow window = GetWindow<BattleShowTileObjEditorWindow>("Tile Obj Editor");
        window.minSize = new Vector2(760f, 720f);
        window.Show();
    }

    [MenuItem("Tools/Battle/Show/Restore Legacy Default Floor Data")]
    public static void RestoreLegacyFloorMenu()
    {
        CreateRecoveredFloorTemplateAsset();
    }

    private void OnSelectionChange()
    {
        if (Selection.activeObject is BattleShowSetLayoutSO selected)
        {
            SetLayout(selected);
            Repaint();
        }
    }

    private void OnGUI()
    {
        DrawTopToolbar();
        if (layout == null)
        {
            EditorGUILayout.HelpBox(
                "Show Set Layout SO를 선택하거나 새로 만드세요. 기존 BattleShowFloorTemplateSO는 전투/기계식 바닥용으로 그대로 유지됩니다.",
                MessageType.Info);
            DrawRecoveryBox();
            return;
        }

        EnsureSerializedObject();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawLayoutDataSections();
        DrawPalette();
        DrawCanvas();
        DrawSelectedEntryInspector();
        DrawRecoveryBox();
        EditorGUILayout.EndScrollView();

        HandleKeyboardShortcuts();
    }

    private void DrawTopToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        BattleShowSetLayoutSO picked = (BattleShowSetLayoutSO)EditorGUILayout.ObjectField(
            layout,
            typeof(BattleShowSetLayoutSO),
            false,
            GUILayout.MinWidth(240f));
        if (picked != layout)
            SetLayout(picked);

        if (GUILayout.Button("New Layout", EditorStyles.toolbarButton, GUILayout.Width(82f)))
            CreateNewLayout();

        GUI.enabled = layout != null;
        if (GUILayout.Button("Assign To Battle", EditorStyles.toolbarButton, GUILayout.Width(104f)))
            AssignLayoutToBattleSystems();
        if (GUILayout.Button("Refresh Runtime", EditorStyles.toolbarButton, GUILayout.Width(104f)))
            RefreshRuntime();
        if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(52f)))
        {
            EditorUtility.SetDirty(layout);
            AssetDatabase.SaveAssets();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLayoutDataSections()
    {
        serializedLayout.Update();

        showPresenterSection = EditorGUILayout.Foldout(showPresenterSection, "사회자 / Animator", true);
        if (showPresenterSection)
        {
            EditorGUI.indentLevel++;
            DrawProperty("presenterSprite");
            DrawProperty("presenterAnimatorController");
            DrawProperty("presenterAnimationFrames", true);
            DrawProperty("presenterFps");
            DrawProperty("presenterLoop");
            DrawProperty("presenterFlipX");
            DrawProperty("presenterWorldHeight");
            DrawProperty("presenterPadYOffset");
            DrawProperty("presenterRightPadding");
            EditorGUI.indentLevel--;
        }

        showFloorSection = EditorGUILayout.Foldout(showFloorSection, "Show Floor", true);
        if (showFloorSection)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "여기 Floor Sprite는 ScreenCarrier_10x2 / PresenterCarrier_6x4에만 적용됩니다. 전투 MapBlock에는 적용하지 않습니다.",
                MessageType.None);
            DrawProperty("showFloorSprites", true);
            DrawProperty("showFloorTint");
            DrawProperty("cellWorldSize");
            DrawProperty("worldOffset");
            EditorGUI.indentLevel--;
        }

        serializedLayout.ApplyModifiedProperties();
    }

    private void DrawPalette()
    {
        serializedLayout.Update();
        showPaletteSection = EditorGUILayout.Foldout(showPaletteSection, "Tile Object Palette", true);
        if (!showPaletteSection)
            return;

        EditorGUI.indentLevel++;
        EditorGUILayout.PropertyField(serializedLayout.FindProperty("tilePalette"), true);
        serializedLayout.ApplyModifiedProperties();
        EditorGUI.indentLevel--;

        Sprite[] palette = layout.TilePalette;
        if (palette == null || palette.Length == 0)
        {
            EditorGUILayout.HelpBox("Tile Palette에 외곽 치장용 Sprite를 넣으면 아래 캔버스에서 브러시로 배치할 수 있습니다.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Toggle(brushSprite == null, "Select", "Button", GUILayout.Width(64f)))
            brushSprite = null;

        for (int i = 0; i < palette.Length; i++)
        {
            Sprite sprite = palette[i];
            if (sprite == null)
                continue;

            GUIContent content = new(AssetPreview.GetMiniThumbnail(sprite), sprite.name);
            bool selected = brushSprite == sprite;
            bool pressed = GUILayout.Toggle(selected, content, "Button", GUILayout.Width(42f), GUILayout.Height(42f));
            if (pressed && !selected)
                brushSprite = sprite;
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        replaceCellOnPaint = EditorGUILayout.ToggleLeft("Paint 시 같은 Grid Cell의 기존 오브젝트 교체", replaceCellOnPaint, GUILayout.Width(300f));
        GUILayout.FlexibleSpace();
        EditorGUILayout.LabelField("LMB: 배치/선택 · Drag: 이동 · RMB: 최상단 삭제 · MMB: Pan · Wheel: Zoom", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawCanvas()
    {
        Rect rect = GUILayoutUtility.GetRect(100f, CanvasHeight, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.11f, 0.11f, 0.12f, 1f));

        Event evt = Event.current;
        HandleCanvasNavigation(evt, rect);

        float cellPixels = BaseCellPixels * zoom;
        DrawGrid(rect, cellPixels);
        DrawReferenceAreas(rect, cellPixels);
        DrawPlacedObjects(rect, cellPixels);
        HandleCanvasEditing(evt, rect, cellPixels);
        DrawCanvasBorder(rect);
    }

    private void DrawGrid(Rect rect, float cellPixels)
    {
        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = new Color(0.24f, 0.24f, 0.26f, 0.55f);

        for (int x = GridMinX; x <= GridMaxX + 1; x++)
        {
            Vector2 a = GridToScreen(new Vector2(x - 0.5f, GridMinY - 0.5f), rect, cellPixels);
            Vector2 b = GridToScreen(new Vector2(x - 0.5f, GridMaxY + 0.5f), rect, cellPixels);
            Handles.DrawLine(a, b);
        }
        for (int y = GridMinY; y <= GridMaxY + 1; y++)
        {
            Vector2 a = GridToScreen(new Vector2(GridMinX - 0.5f, y - 0.5f), rect, cellPixels);
            Vector2 b = GridToScreen(new Vector2(GridMaxX + 0.5f, y - 0.5f), rect, cellPixels);
            Handles.DrawLine(a, b);
        }

        Handles.color = old;
        Handles.EndGUI();
    }

    private void DrawReferenceAreas(Rect rect, float cellPixels)
    {
        DrawGuideRect(rect, cellPixels, new Rect(-0.5f, -0.5f, 4f, 4f), new Color(0.30f, 0.55f, 0.85f, 0.16f), "Persistent Base 4x4");
        DrawGuideRect(rect, cellPixels, new Rect(-0.5f, 3.5f, 10f, 2f), new Color(0.75f, 0.55f, 0.25f, 0.16f), "Screen Carrier 10x2");
        DrawGuideRect(rect, cellPixels, new Rect(3.5f, -0.5f, 6f, 4f), new Color(0.50f, 0.75f, 0.35f, 0.14f), "Presenter Carrier 6x4");
    }

    private void DrawGuideRect(Rect canvas, float cellPixels, Rect gridRect, Color fill, string label)
    {
        Vector2 topLeft = GridToScreen(new Vector2(gridRect.xMin, gridRect.yMax), canvas, cellPixels);
        Vector2 bottomRight = GridToScreen(new Vector2(gridRect.xMax, gridRect.yMin), canvas, cellPixels);
        Rect screen = Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        EditorGUI.DrawRect(screen, fill);
        GUI.Label(new Rect(screen.x + 4f, screen.y + 2f, screen.width - 8f, 18f), label, EditorStyles.miniLabel);
    }

    private void DrawPlacedObjects(Rect rect, float cellPixels)
    {
        IReadOnlyList<BattleShowTileObjectData> entries = layout.TileObjects;
        List<int> order = new(entries.Count);
        for (int i = 0; i < entries.Count; i++) order.Add(i);
        order.Sort((a, b) =>
        {
            int sort = entries[a].SortingOrder.CompareTo(entries[b].SortingOrder);
            return sort != 0 ? sort : a.CompareTo(b);
        });

        for (int n = 0; n < order.Count; n++)
        {
            int index = order[n];
            BattleShowTileObjectData entry = entries[index];
            if (entry == null || entry.Sprite == null)
                continue;

            Rect tileRect = GetEntryScreenRect(entry, rect, cellPixels);
            DrawSprite(entry.Sprite, tileRect, entry.Tint);
            if (index == selectedIndex)
                DrawSelectionOutline(tileRect);
        }
    }

    private static void DrawSprite(Sprite sprite, Rect rect, Color tint)
    {
        if (sprite == null || sprite.texture == null)
            return;

        Rect tr = sprite.textureRect;
        Texture2D tex = sprite.texture;
        Rect uv = new(
            tr.x / tex.width,
            tr.y / tex.height,
            tr.width / tex.width,
            tr.height / tex.height);

        Color old = GUI.color;
        GUI.color = tint;
        GUI.DrawTextureWithTexCoords(rect, tex, uv, true);
        GUI.color = old;
    }

    private static void DrawSelectionOutline(Rect rect)
    {
        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = Color.yellow;
        Vector3[] points =
        {
            new(rect.xMin, rect.yMin), new(rect.xMax, rect.yMin),
            new(rect.xMax, rect.yMax), new(rect.xMin, rect.yMax),
            new(rect.xMin, rect.yMin)
        };
        Handles.DrawAAPolyLine(2f, points);
        Handles.color = old;
        Handles.EndGUI();
    }

    private static void DrawCanvasBorder(Rect rect)
    {
        Handles.BeginGUI();
        Color old = Handles.color;
        Handles.color = new Color(0.42f, 0.42f, 0.45f, 1f);
        Vector3[] points =
        {
            new(rect.xMin, rect.yMin), new(rect.xMax, rect.yMin),
            new(rect.xMax, rect.yMax), new(rect.xMin, rect.yMax),
            new(rect.xMin, rect.yMin)
        };
        Handles.DrawAAPolyLine(1f, points);
        Handles.color = old;
        Handles.EndGUI();
    }

    private void HandleCanvasNavigation(Event evt, Rect rect)
    {
        if (!rect.Contains(evt.mousePosition))
            return;

        if (evt.type == EventType.ScrollWheel)
        {
            float oldZoom = zoom;
            zoom = Mathf.Clamp(zoom * (evt.delta.y > 0f ? 0.9f : 1.1f), 0.35f, 2.6f);
            if (!Mathf.Approximately(oldZoom, zoom))
                Repaint();
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && evt.button == 2)
        {
            pan += evt.delta;
            Repaint();
            evt.Use();
        }
    }

    private void HandleCanvasEditing(Event evt, Rect rect, float cellPixels)
    {
        if (!rect.Contains(evt.mousePosition))
            return;

        Vector2Int cell = ScreenToGrid(evt.mousePosition, rect, cellPixels);

        if (evt.type == EventType.MouseDown && evt.button == 0)
        {
            if (brushSprite != null && !evt.control && !evt.command)
                PaintCell(cell);
            else
                selectedIndex = FindTopEntryAtCell(cell);

            GUI.FocusControl(null);
            Repaint();
            evt.Use();
        }
        else if (evt.type == EventType.MouseDrag && evt.button == 0 && brushSprite == null && selectedIndex >= 0)
        {
            IReadOnlyList<BattleShowTileObjectData> entries = layout.TileObjects;
            if (selectedIndex < entries.Count && entries[selectedIndex].GridPosition != cell)
            {
                Undo.RecordObject(layout, "Move Show Tile Object");
                entries[selectedIndex].GridPosition = cell;
                MarkLayoutDirty();
            }
            evt.Use();
        }
        else if (evt.type == EventType.MouseDown && evt.button == 1)
        {
            int index = FindTopEntryAtCell(cell);
            if (index >= 0)
                RemoveEntry(index, "Erase Show Tile Object");
            evt.Use();
        }
    }

    private void PaintCell(Vector2Int cell)
    {
        Undo.RecordObject(layout, "Paint Show Tile Object");
        if (replaceCellOnPaint)
        {
            for (int i = layout.TileObjects.Count - 1; i >= 0; i--)
                if (layout.TileObjects[i].GridPosition == cell)
                    layout.RemoveTileObjectAt(i);
        }

        layout.AddTileObject(brushSprite, cell);
        selectedIndex = layout.TileObjects.Count - 1;
        MarkLayoutDirty();
    }

    private int FindTopEntryAtCell(Vector2Int cell)
    {
        IReadOnlyList<BattleShowTileObjectData> entries = layout.TileObjects;
        int best = -1;
        int bestSorting = int.MinValue;
        for (int i = 0; i < entries.Count; i++)
        {
            BattleShowTileObjectData entry = entries[i];
            if (entry == null || entry.GridPosition != cell)
                continue;
            if (best < 0 || entry.SortingOrder > bestSorting || (entry.SortingOrder == bestSorting && i > best))
            {
                best = i;
                bestSorting = entry.SortingOrder;
            }
        }
        return best;
    }

    private void DrawSelectedEntryInspector()
    {
        if (selectedIndex < 0 || selectedIndex >= layout.TileObjects.Count)
            return;

        BattleShowTileObjectData entry = layout.TileObjects[selectedIndex];
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField($"Selected Tile Object #{selectedIndex}", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        string label = EditorGUILayout.TextField("Label", entry.Label);
        Sprite sprite = (Sprite)EditorGUILayout.ObjectField("Sprite", entry.Sprite, typeof(Sprite), false);
        Vector2Int grid = EditorGUILayout.Vector2IntField("Grid Position", entry.GridPosition);
        Vector2 offset = EditorGUILayout.Vector2Field("Local Offset", entry.LocalOffset);
        int turns = EditorGUILayout.IntSlider("Quarter Turns", entry.QuarterTurns, 0, 3);
        bool flipX = EditorGUILayout.Toggle("Flip X", entry.FlipX);
        bool flipY = EditorGUILayout.Toggle("Flip Y", entry.FlipY);
        Vector2 scale = EditorGUILayout.Vector2Field("Scale", entry.Scale);
        Color tint = EditorGUILayout.ColorField("Tint", entry.Tint);
        int sorting = EditorGUILayout.IntField("Sorting Order", entry.SortingOrder);
        bool reward = EditorGUILayout.Toggle("Visible In Reward", entry.VisibleInReward);
        bool map = EditorGUILayout.Toggle("Visible In Map", entry.VisibleInMap);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(layout, "Edit Show Tile Object");
            entry.Label = label;
            entry.Sprite = sprite;
            entry.GridPosition = grid;
            entry.LocalOffset = offset;
            entry.QuarterTurns = turns;
            entry.FlipX = flipX;
            entry.FlipY = flipY;
            entry.Scale = scale;
            entry.Tint = tint;
            entry.SortingOrder = sorting;
            entry.VisibleInReward = reward;
            entry.VisibleInMap = map;
            MarkLayoutDirty();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rotate 90°"))
        {
            Undo.RecordObject(layout, "Rotate Show Tile Object");
            entry.QuarterTurns++;
            MarkLayoutDirty();
        }
        if (GUILayout.Button("Flip X"))
        {
            Undo.RecordObject(layout, "Flip Show Tile Object X");
            entry.FlipX = !entry.FlipX;
            MarkLayoutDirty();
        }
        if (GUILayout.Button("Flip Y"))
        {
            Undo.RecordObject(layout, "Flip Show Tile Object Y");
            entry.FlipY = !entry.FlipY;
            MarkLayoutDirty();
        }
        if (GUILayout.Button("Duplicate"))
        {
            Undo.RecordObject(layout, "Duplicate Show Tile Object");
            selectedIndex = layout.DuplicateTileObjectAt(selectedIndex);
            MarkLayoutDirty();
        }
        if (GUILayout.Button("Delete"))
            RemoveEntry(selectedIndex, "Delete Show Tile Object");
        EditorGUILayout.EndHorizontal();
    }

    private void HandleKeyboardShortcuts()
    {
        Event evt = Event.current;
        if (evt.type != EventType.KeyDown || layout == null || selectedIndex < 0 || selectedIndex >= layout.TileObjects.Count)
            return;

        BattleShowTileObjectData entry = layout.TileObjects[selectedIndex];
        if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
        {
            RemoveEntry(selectedIndex, "Delete Show Tile Object");
            evt.Use();
        }
        else if (evt.keyCode == KeyCode.R)
        {
            Undo.RecordObject(layout, "Rotate Show Tile Object");
            entry.QuarterTurns++;
            MarkLayoutDirty();
            evt.Use();
        }
        else if (evt.keyCode == KeyCode.X)
        {
            Undo.RecordObject(layout, "Flip Show Tile Object X");
            entry.FlipX = !entry.FlipX;
            MarkLayoutDirty();
            evt.Use();
        }
        else if (evt.keyCode == KeyCode.Y)
        {
            Undo.RecordObject(layout, "Flip Show Tile Object Y");
            entry.FlipY = !entry.FlipY;
            MarkLayoutDirty();
            evt.Use();
        }
    }

    private void RemoveEntry(int index, string undoName)
    {
        if (index < 0 || index >= layout.TileObjects.Count)
            return;
        Undo.RecordObject(layout, undoName);
        layout.RemoveTileObjectAt(index);
        selectedIndex = Mathf.Clamp(index - 1, -1, layout.TileObjects.Count - 1);
        MarkLayoutDirty();
    }

    private void DrawRecoveryBox()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Legacy Default Floor 복구", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Git 과거 기록에서 확인된 기본값은 Floor -18 / Lower -17 / Upper -15 / Handle -14, " +
            "Tint White, Handle ContactSideOnly, Distance 1, Dock Punch 0.14 / 0.14 / 4입니다. " +
            "과거 Git에는 사용자 Floor Sprite 바이너리/참조가 커밋되어 있지 않아 이 버튼은 Sprite 슬롯을 임의로 만들거나 덮어쓰지 않습니다.",
            EditorStyles.wordWrappedLabel);
        if (GUILayout.Button("Create Recovered Default Floor Template..."))
            CreateRecoveredFloorTemplateAsset();
        EditorGUILayout.EndVertical();

        if (layout != null)
        {
            EditorGUILayout.Space(4f);
            if (GUILayout.Button("Clear ALL Tile Objects..."))
            {
                if (EditorUtility.DisplayDialog(
                        "Clear Tile Objects",
                        "이 Show Set Layout에 배치된 Tile Object 데이터만 전부 지웁니다. 원본 Sprite/Asset 파일은 삭제하지 않습니다.",
                        "Clear Layout Data",
                        "Cancel"))
                {
                    Undo.RecordObject(layout, "Clear Show Tile Objects");
                    layout.ClearTileObjects();
                    selectedIndex = -1;
                    MarkLayoutDirty();
                }
            }
        }
    }

    private void DrawProperty(string name, bool includeChildren = false)
    {
        SerializedProperty property = serializedLayout.FindProperty(name);
        if (property != null)
            EditorGUILayout.PropertyField(property, includeChildren);
    }

    private void EnsureSerializedObject()
    {
        if (serializedLayout == null || serializedLayout.targetObject != layout)
            serializedLayout = new SerializedObject(layout);
    }

    private void SetLayout(BattleShowSetLayoutSO value)
    {
        layout = value;
        serializedLayout = layout != null ? new SerializedObject(layout) : null;
        selectedIndex = -1;
        brushSprite = null;
    }

    private void CreateNewLayout()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Show Set Layout",
            "BattleShowSetLayout_Default",
            "asset",
            "Show Set Layout SO를 저장할 위치를 선택하세요.");
        if (string.IsNullOrEmpty(path))
            return;

        BattleShowSetLayoutSO asset = ScriptableObject.CreateInstance<BattleShowSetLayoutSO>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        SetLayout(asset);
        Selection.activeObject = asset;
    }

    private void AssignLayoutToBattleSystems()
    {
        BattleSceneManager sceneManager = UnityEngine.Object.FindFirstObjectByType<BattleSceneManager>(FindObjectsInactive.Include);
        if (sceneManager == null)
        {
            EditorUtility.DisplayDialog("Tile Obj Editor", "현재 로드된 씬에서 BattleSceneManager를 찾지 못했습니다.", "OK");
            return;
        }

        BattleShowSetDecorationController controller = sceneManager.GetComponent<BattleShowSetDecorationController>();
        if (controller == null)
            controller = Undo.AddComponent<BattleShowSetDecorationController>(sceneManager.gameObject);

        Undo.RecordObject(controller, "Assign Show Set Layout");
        SerializedObject serializedController = new(controller);
        SerializedProperty property = serializedController.FindProperty("showSetLayout");
        property.objectReferenceValue = layout;
        serializedController.ApplyModifiedProperties();
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(sceneManager.gameObject.scene);
        Selection.activeObject = controller;
    }

    private void RefreshRuntime()
    {
        BattleShowSetDecorationController controller = UnityEngine.Object.FindFirstObjectByType<BattleShowSetDecorationController>(FindObjectsInactive.Include);
        if (controller != null)
            controller.RefreshNow();
    }

    private static void CreateRecoveredFloorTemplateAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Restore Legacy Default Floor Data",
            "BattleShowFloorTemplate_Default_Recovered",
            "asset",
            "Git 과거 기록에서 복구 가능한 기본 Floor 데이터를 새 SO로 만듭니다. 기존 Asset/Sprite는 덮어쓰지 않습니다.");
        if (string.IsNullOrEmpty(path))
            return;

        BattleShowFloorTemplateSO asset = ScriptableObject.CreateInstance<BattleShowFloorTemplateSO>();
        AssetDatabase.CreateAsset(asset, path);

        SerializedObject so = new(asset);
        SetEnum(so, "handlePlacement", 0);
        SetColor(so, "floorTint", Color.white);
        SetColor(so, "plateTint", Color.white);
        SetColor(so, "handleTint", Color.white);
        SetInt(so, "floorSortingOrder", -18);
        SetInt(so, "lowerPlateSortingOrder", -17);
        SetInt(so, "upperPlateSortingOrder", -15);
        SetInt(so, "handleSortingOrder", -14);
        SetFloat(so, "sideHandleDistance", 1f);
        SetFloat(so, "verticalHandleDistance", 1f);
        SetFloat(so, "dockHandlePunch", 0.14f);
        SetFloat(so, "dockHandlePunchDuration", 0.14f);
        SetInt(so, "dockHandlePunchVibrato", 4);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        BattleShowPresentationManager manager = UnityEngine.Object.FindFirstObjectByType<BattleShowPresentationManager>(FindObjectsInactive.Include);
        if (manager != null && manager.DefaultFloorTemplate == null)
        {
            Undo.RecordObject(manager, "Assign Recovered Default Floor Template");
            SerializedObject managerSo = new(manager);
            SerializedProperty property = managerSo.FindProperty("defaultFloorTemplate");
            property.objectReferenceValue = asset;
            managerSo.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            if (manager.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }

        Selection.activeObject = asset;
        EditorGUIUtility.PingObject(asset);
    }

    private static void SetFloat(SerializedObject so, string name, float value)
    {
        SerializedProperty property = so.FindProperty(name);
        if (property != null) property.floatValue = value;
    }

    private static void SetInt(SerializedObject so, string name, int value)
    {
        SerializedProperty property = so.FindProperty(name);
        if (property != null) property.intValue = value;
    }

    private static void SetEnum(SerializedObject so, string name, int value)
    {
        SerializedProperty property = so.FindProperty(name);
        if (property != null) property.enumValueIndex = value;
    }

    private static void SetColor(SerializedObject so, string name, Color value)
    {
        SerializedProperty property = so.FindProperty(name);
        if (property != null) property.colorValue = value;
    }

    private void MarkLayoutDirty()
    {
        EditorUtility.SetDirty(layout);
        serializedLayout = new SerializedObject(layout);
        Repaint();
    }

    private Rect GetEntryScreenRect(BattleShowTileObjectData entry, Rect canvas, float cellPixels)
    {
        float cellWorld = layout.CellWorldSize;
        Vector2 centerGrid = new(
            entry.GridPosition.x + entry.LocalOffset.x / cellWorld,
            entry.GridPosition.y + entry.LocalOffset.y / cellWorld);
        Vector2 center = GridToScreen(centerGrid, canvas, cellPixels);

        Vector3 spriteBounds = entry.Sprite != null ? entry.Sprite.bounds.size : Vector3.one;
        Vector2 spriteWorld = new(spriteBounds.x, spriteBounds.y);
        Vector2 size = new(
            Mathf.Max(0.2f, Mathf.Abs(spriteWorld.x * entry.Scale.x / cellWorld)) * cellPixels,
            Mathf.Max(0.2f, Mathf.Abs(spriteWorld.y * entry.Scale.y / cellWorld)) * cellPixels);
        if ((entry.QuarterTurns & 1) != 0)
        {
            float width = size.x;
            size.x = size.y;
            size.y = width;
        }

        return new Rect(center.x - size.x * 0.5f, center.y - size.y * 0.5f, size.x, size.y);
    }

    private Vector2 GridToScreen(Vector2 grid, Rect canvas, float cellPixels)
    {
        Vector2 viewCenterGrid = new(4.5f, 2.5f);
        Vector2 delta = grid - viewCenterGrid;
        return canvas.center + pan + new Vector2(delta.x * cellPixels, -delta.y * cellPixels);
    }

    private Vector2Int ScreenToGrid(Vector2 screen, Rect canvas, float cellPixels)
    {
        Vector2 local = screen - canvas.center - pan;
        Vector2 grid = new(4.5f + local.x / cellPixels, 2.5f - local.y / cellPixels);
        return new Vector2Int(Mathf.RoundToInt(grid.x), Mathf.RoundToInt(grid.y));
    }
}
#endif
