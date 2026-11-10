using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixelEffects.Editor
{
    /// <summary>
    /// Window > PixelFX Gallery: every effect in the project, playing. Search, filter by category, drag one into
    /// the scene or the hierarchy, double-click to add it where the Scene view is looking.
    /// </summary>
    public sealed class PixelFxGalleryWindow : EditorWindow
    {
        sealed class Thumb
        {
            public Texture2D canvas;
            public int frame = -2;
            public int usedAt;
        }

        const float Gap = 8f, LabelHeight = 18f, FooterHeight = 104f;
        const int MaxThumbs = 96;

        readonly List<PixelFxAsset> all = new List<PixelFxAsset>();
        readonly List<PixelFxAsset> shown = new List<PixelFxAsset>();
        readonly List<string> categoryIds = new List<string>();
        readonly Dictionary<PixelFxAsset, Thumb> thumbs = new Dictionary<PixelFxAsset, Thumb>();
        string[] categoryNames = { "All" };

        [SerializeField] string search = "";
        [SerializeField] string category = "";
        [SerializeField] float cell = 128f;
        [SerializeField] bool animate = true;
        [SerializeField] PixelFxAsset selected;

        Vector2 scroll;
        Color32[] pixels;
        bool visible = true, dirty = true;
        int paint;
        double lastPaint;
        PixelFxAsset pressed;
        Vector2 pressedAt;

        [MenuItem("Window/PixelFX Gallery")]
        [MenuItem("Tools/PixelFX/Gallery", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<PixelFxGalleryWindow>();
            window.titleContent = new GUIContent("PixelFX Gallery");
            window.minSize = new Vector2(360f, 320f);
            window.Show();
        }

        void OnEnable()
        {
            titleContent = new GUIContent("PixelFX Gallery");
            EditorApplication.update += OnUpdate;
            EditorApplication.projectChanged += MarkDirty;
            dirty = true;
        }

        void OnDisable()
        {
            EditorApplication.update -= OnUpdate;
            EditorApplication.projectChanged -= MarkDirty;
            foreach (var t in thumbs.Values)
                if (t.canvas != null)
                    DestroyImmediate(t.canvas);
            thumbs.Clear();
        }

        void OnBecameVisible() => visible = true;
        void OnBecameInvisible() => visible = false;
        void MarkDirty() => dirty = true;

        void OnUpdate()
        {
            // 30 repaints a second while there is something moving to look at
            if (!visible || !animate || all.Count == 0)
                return;
            double now = EditorApplication.timeSinceStartup;
            if (now - lastPaint < 1.0 / 30.0)
                return;
            lastPaint = now;
            Repaint();
        }

        // ---- data

        void Reload()
        {
            dirty = false;
            all.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(PixelFxAsset)))
            {
                var fx = AssetDatabase.LoadAssetAtPath<PixelFxAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (fx != null && fx.FrameCount > 0)
                    all.Add(fx);
            }

            // categories in the libraries' order (the gallery order of pixelfx), then whatever else turns up
            categoryIds.Clear();
            var names = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(PixelFxLibrary)))
            {
                var library = AssetDatabase.LoadAssetAtPath<PixelFxLibrary>(AssetDatabase.GUIDToAssetPath(guid));
                if (library == null)
                    continue;
                foreach (var c in library.categories)
                    if (!names.ContainsKey(c.id))
                    {
                        names[c.id] = string.IsNullOrEmpty(c.name) ? c.id : c.name;
                        categoryIds.Add(c.id);
                    }
            }
            var extra = new List<string>();
            foreach (var fx in all)
                if (!string.IsNullOrEmpty(fx.category) && !names.ContainsKey(fx.category))
                {
                    names[fx.category] = fx.category;
                    extra.Add(fx.category);
                }
            extra.Sort(StringComparer.Ordinal);
            categoryIds.AddRange(extra);

            var order = new Dictionary<string, int>();
            for (int i = 0; i < categoryIds.Count; i++)
                order[categoryIds[i]] = i;
            all.Sort((a, b) =>
            {
                int ca = order.TryGetValue(a.category ?? "", out var x) ? x : int.MaxValue;
                int cb = order.TryGetValue(b.category ?? "", out var y) ? y : int.MaxValue;
                return ca != cb ? ca.CompareTo(cb) : string.CompareOrdinal(a.id, b.id);
            });

            categoryNames = new string[categoryIds.Count + 1];
            categoryNames[0] = $"All ({all.Count})";
            for (int i = 0; i < categoryIds.Count; i++)
                categoryNames[i + 1] = names[categoryIds[i]];
            if (categoryIds.IndexOf(category) < 0)
                category = "";
            Filter();
        }

        void Filter()
        {
            shown.Clear();
            var words = search.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var fx in all)
            {
                if (category.Length > 0 && fx.category != category)
                    continue;
                bool match = true;
                foreach (var w in words)
                    if (!Matches(fx, w))
                    {
                        match = false;
                        break;
                    }
                if (match)
                    shown.Add(fx);
            }
        }

        static bool Matches(PixelFxAsset fx, string word)
        {
            if ((fx.id ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if ((fx.displayName ?? "").IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (fx.tags != null)
                foreach (var t in fx.tags)
                    if (t != null && t.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
            return false;
        }

        // ---- GUI

        void OnGUI()
        {
            if (dirty)
                Reload();
            paint++;

            DrawToolbar();
            if (all.Count == 0)
            {
                EditorGUILayout.HelpBox("No effects found in this project. If the PixelFX package is embedded, run Tools > PixelFX > Rebuild Effects.", MessageType.Info);
                return;
            }

            float footer = selected != null ? FooterHeight : 0f;
            var gridRect = new Rect(0f, EditorStyles.toolbar.fixedHeight, position.width, position.height - EditorStyles.toolbar.fixedHeight - footer);
            DrawGrid(gridRect);
            if (selected != null)
                DrawFooter(new Rect(0f, gridRect.yMax, position.width, footer));
            TrimThumbs();
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                int index = categoryIds.IndexOf(category) + 1;
                EditorGUI.BeginChangeCheck();
                index = EditorGUILayout.Popup(index, categoryNames, EditorStyles.toolbarPopup, GUILayout.Width(130f));
                search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(80f));
                if (EditorGUI.EndChangeCheck())
                {
                    category = index > 0 ? categoryIds[index - 1] : "";
                    scroll = Vector2.zero;
                    Filter();
                }
                GUILayout.Label(shown.Count.ToString(), EditorStyles.miniLabel, GUILayout.Width(30f));
                cell = GUILayout.HorizontalSlider(cell, 64f, 320f, GUILayout.Width(80f));
                animate = GUILayout.Toggle(animate, "Play", EditorStyles.toolbarButton, GUILayout.Width(40f));
            }
        }

        void DrawGrid(Rect area)
        {
            float size = Mathf.Round(cell);
            int columns = Mathf.Max(1, Mathf.FloorToInt((area.width - Gap - 14f) / (size + Gap)));
            int rows = (shown.Count + columns - 1) / columns;
            float rowHeight = size + LabelHeight + Gap;
            var content = new Rect(0f, 0f, area.width - 14f, rows * rowHeight + Gap);
            float inset = Mathf.Max(Gap, Mathf.Floor((content.width - columns * (size + Gap) + Gap) * 0.5f)); // centre the grid

            var e = Event.current;
            double now = EditorApplication.timeSinceStartup;
            scroll = GUI.BeginScrollView(area, scroll, content);
            int first = Mathf.Max(0, Mathf.FloorToInt((scroll.y - Gap) / rowHeight));
            int last = Mathf.Min(rows - 1, Mathf.FloorToInt((scroll.y + area.height) / rowHeight));
            for (int row = first; row <= last; row++)
                for (int col = 0; col < columns; col++)
                {
                    int i = row * columns + col;
                    if (i >= shown.Count)
                        break;
                    var fx = shown[i];
                    var box = new Rect(inset + col * (size + Gap), Gap + row * rowHeight, size, size);
                    var label = new Rect(box.x, box.yMax, size, LabelHeight);

                    if (e.type == EventType.Repaint)
                    {
                        GUI.DrawTexture(box, PixelFxPreview.BackdropTexture, ScaleMode.StretchToFill, false);
                        var thumb = ThumbFor(fx, animate ? PixelFxPreview.FrameAt(fx, now) : fx.previewFrame);
                        GUI.DrawTexture(PixelFxPreview.Fit(fx, box), thumb.canvas, ScaleMode.StretchToFill, false);
                        if (fx == selected)
                            Outline(box, new Color(0.96f, 0.24f, 0.82f));
                        GUI.Label(label, new GUIContent(fx.displayName, fx.description), fx == selected ? Styles.LabelSelected : Styles.Label);
                    }

                    var hit = new Rect(box.x, box.y, size, size + LabelHeight);
                    if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
                    {
                        selected = fx;
                        pressed = fx;
                        pressedAt = e.mousePosition;
                        if (e.clickCount == 2)
                        {
                            AddToScene(fx);
                            pressed = null;
                        }
                        else
                            EditorGUIUtility.PingObject(fx);
                        e.Use();
                        Repaint();
                    }
                }

            if (e.type == EventType.MouseDrag && pressed != null && (e.mousePosition - pressedAt).sqrMagnitude > 16f)
            {
                var prefab = PrefabOf(pressed);
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.objectReferences = new UnityEngine.Object[] { prefab != null ? prefab : pressed };
                DragAndDrop.StartDrag(pressed.displayName);
                pressed = null;
                e.Use();
            }
            if (e.rawType == EventType.MouseUp)
                pressed = null;
            GUI.EndScrollView();
        }

        void DrawFooter(Rect area)
        {
            var fx = selected;
            EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 1f), new Color(0f, 0f, 0f, 0.35f));
            GUILayout.BeginArea(new Rect(area.x + Gap, area.y + 6f, area.width - Gap * 2f, area.height - 8f));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(fx.displayName, EditorStyles.boldLabel);
                GUILayout.Label(fx.id, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Asset", EditorStyles.miniButton, GUILayout.Width(52f)))
                    Selection.activeObject = fx;
                using (new EditorGUI.DisabledScope(PrefabOf(fx) == null))
                    if (GUILayout.Button("Prefab", EditorStyles.miniButton, GUILayout.Width(52f)))
                        Selection.activeObject = PrefabOf(fx);
                if (GUILayout.Button("Add to Scene", EditorStyles.miniButton, GUILayout.Width(88f)))
                    AddToScene(fx);
            }
            GUILayout.Label(PixelFxAssetEditor.Summary(fx), EditorStyles.miniLabel);
            GUILayout.Label(fx.description, Styles.Description);
            if (fx.tags != null && fx.tags.Length > 0)
                GUILayout.Label(string.Join("  ", fx.tags), EditorStyles.miniLabel);
            GUILayout.EndArea();
        }

        static void Outline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, 2f), c);
            EditorGUI.DrawRect(new Rect(r.x - 2f, r.yMax, r.width + 4f, 2f), c);
            EditorGUI.DrawRect(new Rect(r.x - 2f, r.y, 2f, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax, r.y, 2f, r.height), c);
        }

        // ---- thumbnails

        Thumb ThumbFor(PixelFxAsset fx, int frame)
        {
            if (!thumbs.TryGetValue(fx, out var thumb) || thumb.canvas == null || thumb.canvas.width != fx.size.x || thumb.canvas.height != fx.size.y)
            {
                if (thumb != null && thumb.canvas != null)
                    DestroyImmediate(thumb.canvas);
                thumb = new Thumb { canvas = PixelFxPreview.NewCanvas(fx) };
                thumbs[fx] = thumb;
            }
            if (thumb.frame != frame)
            {
                thumb.frame = frame;
                PixelFxPreview.Render(fx, frame, thumb.canvas, ref pixels);
            }
            thumb.usedAt = paint;
            return thumb;
        }

        // Keep the textures of what is on screen, drop the rest once there are too many.
        void TrimThumbs()
        {
            if (thumbs.Count <= MaxThumbs || Event.current.type != EventType.Repaint)
                return;
            var drop = new List<PixelFxAsset>();
            foreach (var kv in thumbs)
                if (kv.Value.usedAt != paint)
                    drop.Add(kv.Key);
            foreach (var fx in drop)
            {
                if (thumbs[fx].canvas != null)
                    DestroyImmediate(thumbs[fx].canvas);
                thumbs.Remove(fx);
            }
        }

        // ---- actions

        static GameObject PrefabOf(PixelFxAsset fx)
        {
            var path = AssetDatabase.GetAssetPath(fx);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(Path.ChangeExtension(path, ".prefab"));
        }

        static void AddToScene(PixelFxAsset fx)
        {
            GameObject go;
            var prefab = PrefabOf(fx);
            if (prefab != null)
                go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            else
            {
                go = new GameObject(fx.name);
                go.AddComponent<SpriteRenderer>().sprite = fx.PreviewSprite;
                go.AddComponent<PixelFxPlayer>().Effect = fx;
            }
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                var p = view.pivot;
                if (view.in2DMode)
                    p.z = 0f;
                go.transform.position = p;
            }
            Undo.RegisterCreatedObjectUndo(go, "Add " + fx.displayName);
            Selection.activeGameObject = go;
        }

        static class Styles
        {
            public static readonly GUIStyle Label = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            public static readonly GUIStyle LabelSelected = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            public static readonly GUIStyle Description = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
        }
    }
}
