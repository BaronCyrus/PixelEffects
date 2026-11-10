using System.Collections.Generic;
using UnityEngine;

namespace PixelEffects.Demo
{
    /// <summary>
    /// The demo scene: one category of effects at a time, all playing in a grid. Pick a category along the top
    /// (or with the left and right arrow keys). Click an effect to play it again, acted out: it freezes on
    /// contact and shakes the camera the way its hits say to.
    ///
    /// The camera is set so one sprite pixel covers a whole number of screen pixels, and every effect is placed
    /// on the pixel grid, so the art stays crisp at any window size.
    ///
    /// It draws its labels with OnGUI and reads clicks and keys from OnGUI events, so it runs whichever input
    /// system the project uses.
    /// </summary>
    public sealed class PixelFxShowcase : MonoBehaviour
    {
        [Tooltip("Every effect, by category. In a game, reference only the effects you use instead.")]
        public PixelFxLibrary library;

        public Camera viewCamera;

        [Min(1)] public int columns = 5;

        [Tooltip("The most an effect is magnified, in screen pixels per sprite pixel.")]
        [Min(1)] public int maxZoom = 4;

        [Tooltip("How long one-shots rest before they play again, in seconds.")]
        [Min(0f)] public float repeatDelay = 0.6f;

        [Tooltip("Move to the next category after this many seconds. 0 stays put.")]
        [Min(0f)] public float autoAdvance;

        [Tooltip("The category to open with.")]
        public int category;

        sealed class Cell
        {
            public PixelFxPlayer player;
            public PixelFxAsset effect;
            public Rect area;   // on screen, in GUI coordinates (y down)
            public float scale;
        }

        readonly List<Cell> cells = new List<Cell>();
        int shown = -1, screenWidth, screenHeight;
        float shownAt, shake, shakeAmount;
        Cell focus;
        string[] names;
        GUIStyle labelStyle, titleStyle, blurbStyle, buttonStyle;

        static readonly Vector3 CameraHome = new Vector3(0f, 0f, -10f);

        // GUI sizes follow the window: k is 1 at 540p, 2 at 1080p
        static float GuiScale => Mathf.Max(1f, Mathf.Round(Screen.height / 540f));
        static float HeaderHeight => 74f * GuiScale;
        static float LabelHeight => 18f * GuiScale;

        void Start()
        {
            if (viewCamera == null)
                viewCamera = Camera.main;
            if (library == null || library.categories.Length == 0 || viewCamera == null)
            {
                Debug.LogWarning("PixelFxShowcase needs a library and a camera.", this);
                enabled = false;
                return;
            }
            names = new string[library.categories.Length];
            for (int i = 0; i < names.Length; i++)
                names[i] = library.categories[i].name;
            Show(category);
        }

        void Show(int index)
        {
            int n = library.categories.Length;
            category = ((index % n) + n) % n;
            shown = category;
            shownAt = Time.unscaledTime;
            focus = null;
            shake = 0f;

            foreach (var c in cells)
                if (c.player != null)
                    Destroy(c.player.gameObject);
            cells.Clear();

            var effects = library.categories[category].effects;
            for (int i = 0; i < effects.Length; i++)
            {
                var fx = effects[i];
                if (fx == null)
                    continue;
                var go = new GameObject(fx.name);
                go.transform.SetParent(transform, false);
                go.AddComponent<SpriteRenderer>().sortingOrder = i;
                var player = go.AddComponent<PixelFxPlayer>();
                player.Loop = PixelFxPlayer.LoopMode.Loop;
                player.RepeatDelay = repeatDelay;
                player.Play(fx);

                var c = new Cell { player = player, effect = fx };
                player.Hit += (p, hit) => OnHit(c, hit);
                cells.Add(c);
            }
            Layout();
        }

        // Put the cells on screen. Everything is worked out in screen pixels, then turned into world units.
        void Layout()
        {
            screenWidth = Screen.width;
            screenHeight = Screen.height;
            if (cells.Count == 0)
                return;

            float ppu = Mathf.Max(1f, library.pixelsPerUnit);
            viewCamera.orthographic = true;
            viewCamera.orthographicSize = screenHeight / (2f * ppu); // one sprite pixel = one screen pixel at scale 1
            viewCamera.transform.position = CameraHome;

            int cols = Mathf.Min(columns, cells.Count), rows = (cells.Count + cols - 1) / cols;
            float cw = screenWidth / (float)cols, ch = (screenHeight - HeaderHeight) / rows, margin = 2f * GuiScale;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                var fx = c.effect;
                // centre a short last row
                int row = i / cols, inRow = row == rows - 1 ? cells.Count - row * cols : cols;
                float left = (screenWidth - inRow * cw) * 0.5f + (i % cols) * cw, top = HeaderHeight + row * ch;
                c.area = new Rect(left, top, cw, ch);

                // the largest whole-number zoom that fits above the label, or a whole-number shrink
                float fit = Mathf.Min((cw - margin * 2f) / fx.size.x, (ch - LabelHeight - margin * 2f) / fx.size.y);
                c.scale = fit >= 1f ? Mathf.Min(maxZoom, Mathf.Floor(fit)) : 1f / Mathf.Ceil(1f / fit - 1e-3f);

                // the canvas's top-left corner on a whole screen pixel, then the effect's origin from there
                float x0 = Mathf.Round(left + (cw - fx.size.x * c.scale) * 0.5f);
                float y0 = Mathf.Round(top + (ch - LabelHeight - fx.size.y * c.scale) * 0.5f);
                float px = x0 + fx.origin.x * c.scale, py = y0 + fx.origin.y * c.scale;
                var t = c.player.transform;
                t.position = new Vector3((px - screenWidth * 0.5f) / ppu, (screenHeight * 0.5f - py) / ppu, 0f);
                t.localScale = new Vector3(c.scale, c.scale, 1f);
            }
        }

        void OnHit(Cell c, PixelFxHit hit)
        {
            if (c != focus)
                return;
            // the effect holds itself still (HitStop); the camera shake is ours to do
            shake = hit.shake;
            shakeAmount = hit.amplitude * c.scale / Mathf.Max(1f, library.pixelsPerUnit);
        }

        void Update()
        {
            if (shown != category)
                Show(category);
            else if (autoAdvance > 0f && Time.unscaledTime - shownAt > autoAdvance)
                Show(category + 1);
            else if (Screen.width != screenWidth || Screen.height != screenHeight)
                Layout();

            var p = CameraHome;
            if (shake > 0f)
            {
                shake -= Time.unscaledDeltaTime;
                p += (Vector3)(Random.insideUnitCircle * shakeAmount);
            }
            viewCamera.transform.position = p;
        }

        void Replay(Cell c)
        {
            if (focus != null && focus.player != null)
                focus.player.HitStop = false;
            focus = c;
            c.player.HitStop = true;
            c.player.Play();
        }

        void OnGUI()
        {
            if (names == null)
                return;
            float k = GuiScale;
            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Overflow };
                labelStyle.normal.textColor = new Color(0.78f, 0.77f, 0.84f);
                titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
                titleStyle.normal.textColor = Color.white;
                blurbStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
                blurbStyle.normal.textColor = new Color(0.55f, 0.53f, 0.63f);
                buttonStyle = new GUIStyle(GUI.skin.button);
            }
            labelStyle.fontSize = Mathf.RoundToInt(10f * k);
            titleStyle.fontSize = Mathf.RoundToInt(15f * k);
            blurbStyle.fontSize = Mathf.RoundToInt(10f * k);
            buttonStyle.fontSize = Mathf.RoundToInt(10f * k);

            // categories, in two rows
            int perRow = (names.Length + 1) / 2;
            float pad = 6f * k, bw = (Screen.width - pad * 2f) / perRow, bh = 20f * k;
            for (int i = 0; i < names.Length; i++)
            {
                var r = new Rect(pad + (i % perRow) * bw, pad + (i / perRow) * bh, bw - 2f, bh - 2f);
                var was = GUI.backgroundColor;
                if (i == category)
                    GUI.backgroundColor = new Color(0.96f, 0.24f, 0.82f);
                if (GUI.Button(r, names[i], buttonStyle))
                    Show(i);
                GUI.backgroundColor = was;
            }

            var cat = library.categories[category];
            float line = pad + bh * 2f + 2f * k, titleWidth = titleStyle.CalcSize(new GUIContent(cat.name)).x;
            GUI.Label(new Rect(pad, line, titleWidth + 4f, 24f * k), cat.name, titleStyle);
            GUI.Label(new Rect(pad + titleWidth + 10f * k, line, Screen.width, 24f * k),
                $"{cat.blurb}   ·   {cat.effects.Length} effects   ·   click one to play it with its hit-stop   ·   left / right arrows change category", blurbStyle);

            // names under the effects
            foreach (var c in cells)
                GUI.Label(new Rect(c.area.x, c.area.yMax - LabelHeight - 2f * k, c.area.width, LabelHeight), c.effect.displayName, labelStyle);

            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.RightArrow)
                    Show(category + 1);
                else if (e.keyCode == KeyCode.LeftArrow)
                    Show(category - 1);
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                foreach (var c in cells)
                    if (c.area.Contains(e.mousePosition))
                    {
                        Replay(c);
                        break;
                    }
            }
        }
    }
}
