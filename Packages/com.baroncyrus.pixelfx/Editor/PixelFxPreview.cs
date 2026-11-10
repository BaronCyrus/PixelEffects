using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixelEffects.Editor
{
    /// <summary>
    /// Draws effect frames for the editor's own UI (the gallery, inspector previews, thumbnails).
    ///
    /// The sheets are premultiplied with the glow baked in, which IMGUI's straight-alpha blend can't show. So a
    /// frame is composited here, on the CPU, over the dark backdrop pixelfx uses for its own previews, into an
    /// opaque picture any GUI call can draw. It reads the PNG pages straight from disk, so it sees exactly what
    /// was baked whatever the import settings.
    /// </summary>
    public static class PixelFxPreview
    {
        /// <summary>pixelfx's "dark" preview backdrop.</summary>
        public static readonly Color32 Backdrop = new Color32(21, 19, 29, 255);

        const long Budget = 384L << 20; // bytes of decoded pages to keep around

        sealed class Page
        {
            public Color32[] pixels;
            public int width, height;
            public long stamp;
        }

        static readonly Dictionary<string, Page> Pages = new Dictionary<string, Page>();
        static long used, clock;
        static Texture2D backdropTexture;

        /// <summary>A 1x1 texture of the backdrop color, to fill the space round a picture with the very same color.</summary>
        public static Texture2D BackdropTexture
        {
            get
            {
                if (backdropTexture == null)
                {
                    backdropTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                    backdropTexture.SetPixels32(new[] { Backdrop });
                    backdropTexture.Apply(false, true);
                }
                return backdropTexture;
            }
        }

        /// <summary>Forget the decoded pages (sheets were reimported).</summary>
        public static void Clear()
        {
            Pages.Clear();
            used = 0;
        }

        static Page PageOf(Texture2D texture)
        {
            if (texture == null)
                return null;
            var path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
                return null;
            if (Pages.TryGetValue(path, out var page))
            {
                page.stamp = ++clock;
                return page;
            }
            var file = PixelFxImporter.PhysicalPath(path);
            if (!File.Exists(file))
                return null;

            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                if (!decoded.LoadImage(File.ReadAllBytes(file), false))
                    return null;
                page = new Page { pixels = decoded.GetPixels32(), width = decoded.width, height = decoded.height, stamp = ++clock };
            }
            finally
            {
                Object.DestroyImmediate(decoded);
            }

            used += page.pixels.Length * 4L;
            Pages[path] = page;
            while (used > Budget && Pages.Count > 1)
            {
                string oldest = null;
                long stamp = long.MaxValue;
                foreach (var kv in Pages)
                    if (kv.Value.stamp < stamp)
                    {
                        stamp = kv.Value.stamp;
                        oldest = kv.Key;
                    }
                used -= Pages[oldest].pixels.Length * 4L;
                Pages.Remove(oldest);
            }
            return page;
        }

        /// <summary>A texture the size of the effect's canvas, to render frames into.</summary>
        public static Texture2D NewCanvas(PixelFxAsset fx)
        {
            return new Texture2D(Mathf.Max(1, fx.size.x), Mathf.Max(1, fx.size.y), TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        /// <summary>
        /// Composite a frame over the backdrop into <paramref name="pixels"/> (the canvas, row by row from the
        /// bottom, as Texture2D.SetPixels32 wants it). The array is made or resized as needed.
        /// </summary>
        public static void Composite(PixelFxAsset fx, int frame, ref Color32[] pixels)
        {
            int W = Mathf.Max(1, fx.size.x), H = Mathf.Max(1, fx.size.y);
            if (pixels == null || pixels.Length != W * H)
                pixels = new Color32[W * H];
            var bg = Backdrop;
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = bg;

            var sprite = fx.GetFrame(frame);
            if (sprite == null)
                return;
            var page = PageOf(sprite.texture);
            if (page == null)
                return;

            // where the piece sits on the canvas: its pivot is the effect's origin
            var rect = sprite.rect;
            var pivot = sprite.pivot;
            int rx = Mathf.RoundToInt(rect.x), ry = Mathf.RoundToInt(rect.y), rw = Mathf.RoundToInt(rect.width), rh = Mathf.RoundToInt(rect.height);
            int bx = fx.origin.x - Mathf.RoundToInt(pivot.x), by = H - fx.origin.y - Mathf.RoundToInt(pivot.y);
            if (rx < 0 || ry < 0 || rx + rw > page.width || ry + rh > page.height)
                return;

            var src = page.pixels;
            for (int j = 0; j < rh; j++)
            {
                int dy = by + j;
                if (dy < 0 || dy >= H)
                    continue;
                int s = (ry + j) * page.width + rx, d = dy * W + bx;
                for (int i = 0; i < rw; i++)
                {
                    int dx = bx + i;
                    if (dx < 0 || dx >= W)
                        continue;
                    var c = src[s + i];
                    if ((c.r | c.g | c.b | c.a) == 0)
                        continue;
                    // premultiplied color (glow included) over the backdrop
                    int k = 255 - c.a;
                    pixels[d + i] = new Color32(
                        (byte)Mathf.Min(255, c.r + bg.r * k / 255),
                        (byte)Mathf.Min(255, c.g + bg.g * k / 255),
                        (byte)Mathf.Min(255, c.b + bg.b * k / 255),
                        255);
                }
            }
        }

        /// <summary>Render a frame into a canvas made by <see cref="NewCanvas"/>.</summary>
        public static void Render(PixelFxAsset fx, int frame, Texture2D canvas, ref Color32[] pixels)
        {
            Composite(fx, frame, ref pixels);
            canvas.SetPixels32(pixels);
            canvas.Apply(false);
        }

        /// <summary>
        /// The frame to show at an editor time: loops loop, one-shots play, hold a beat, and play again.
        /// </summary>
        public static int FrameAt(PixelFxAsset fx, double time)
        {
            int n = fx.FrameCount;
            if (n == 0)
                return -1;
            double fps = Mathf.Max(1f, fx.frameRate);
            if (fx.loop)
                return (int)((long)(time * fps) % n);
            double length = n / fps, t = time % (length + 0.6);
            return Mathf.Min(n - 1, (int)(t * fps));
        }

        /// <summary>The largest rectangle with the canvas's proportions that fits in <paramref name="area"/>, at a whole-number zoom when there is room.</summary>
        public static Rect Fit(PixelFxAsset fx, Rect area)
        {
            float w = Mathf.Max(1, fx.size.x), h = Mathf.Max(1, fx.size.y);
            float k = Mathf.Min(area.width / w, area.height / h);
            if (k >= 1f)
                k = Mathf.Floor(k);
            float dw = w * k, dh = h * k;
            return new Rect(Mathf.Round(area.x + (area.width - dw) * 0.5f), Mathf.Round(area.y + (area.height - dh) * 0.5f), dw, dh);
        }
    }
}
