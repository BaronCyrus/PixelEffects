using UnityEditor;
using UnityEngine;

namespace PixelEffects.Editor
{
    /// <summary>The inspector of an effect: its data, a live preview underneath, and a thumbnail in the Project window.</summary>
    [CustomEditor(typeof(PixelFxAsset))]
    sealed class PixelFxAssetEditor : UnityEditor.Editor
    {
        Texture2D canvas;
        Color32[] pixels;
        int shown = -2;

        PixelFxAsset Fx => (PixelFxAsset)target;

        public override void OnInspectorGUI()
        {
            var fx = Fx;
            EditorGUILayout.LabelField(string.IsNullOrEmpty(fx.displayName) ? fx.name : fx.displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(Summary(fx), EditorStyles.miniLabel);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }

        internal static string Summary(PixelFxAsset fx)
        {
            string s = $"{fx.size.x} x {fx.size.y} px   {fx.FrameCount} frames @ {fx.frameRate:0.#} fps   {fx.Duration:0.##} s   {(fx.loop ? "loop" : "one-shot")}";
            if (fx.hits != null && fx.hits.Length > 0)
                s += $"   {fx.hits.Length} hit{(fx.hits.Length == 1 ? "" : "s")}";
            return s;
        }

        public override bool HasPreviewGUI() => Fx != null && Fx.FrameCount > 0;

        public override bool RequiresConstantRepaint() => HasPreviewGUI();

        public override GUIContent GetPreviewTitle() => new GUIContent(string.IsNullOrEmpty(Fx.displayName) ? Fx.name : Fx.displayName);

        public override string GetInfoString() => Summary(Fx);

        public override void OnPreviewGUI(Rect r, GUIStyle background)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var fx = Fx;
            if (canvas == null || canvas.width != fx.size.x || canvas.height != fx.size.y)
            {
                if (canvas != null)
                    DestroyImmediate(canvas);
                canvas = PixelFxPreview.NewCanvas(fx);
                shown = -2;
            }
            int frame = PixelFxPreview.FrameAt(fx, EditorApplication.timeSinceStartup);
            if (frame != shown)
            {
                shown = frame;
                PixelFxPreview.Render(fx, frame, canvas, ref pixels);
            }
            GUI.DrawTexture(r, PixelFxPreview.BackdropTexture, ScaleMode.StretchToFill, false);
            GUI.DrawTexture(PixelFxPreview.Fit(fx, r), canvas, ScaleMode.StretchToFill, false);
        }

        public override Texture2D RenderStaticPreview(string assetPath, Object[] subAssets, int width, int height)
        {
            var fx = Fx;
            if (fx == null || fx.FrameCount == 0 || width <= 0 || height <= 0)
                return null;

            Color32[] frame = null;
            PixelFxPreview.Composite(fx, fx.previewFrame, ref frame);
            int W = Mathf.Max(1, fx.size.x), H = Mathf.Max(1, fx.size.y);

            // fit the canvas in the thumbnail, nearest neighbour, on the backdrop
            float k = Mathf.Min(width / (float)W, height / (float)H);
            int dw = Mathf.Max(1, Mathf.RoundToInt(W * k)), dh = Mathf.Max(1, Mathf.RoundToInt(H * k));
            int ox = (width - dw) / 2, oy = (height - dh) / 2;
            var thumb = new Color32[width * height];
            for (int i = 0; i < thumb.Length; i++)
                thumb[i] = PixelFxPreview.Backdrop;
            for (int y = 0; y < dh; y++)
            {
                int sy = Mathf.Min(H - 1, (int)((y + 0.5f) / k));
                for (int x = 0; x < dw; x++)
                    thumb[(oy + y) * width + ox + x] = frame[sy * W + Mathf.Min(W - 1, (int)((x + 0.5f) / k))];
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(thumb);
            texture.Apply();
            return texture;
        }

        void OnDisable()
        {
            if (canvas != null)
                DestroyImmediate(canvas);
            canvas = null;
        }
    }
}
