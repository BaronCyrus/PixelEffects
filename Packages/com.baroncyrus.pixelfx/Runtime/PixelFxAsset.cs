using System;
using UnityEngine;

namespace PixelEffects
{
    /// <summary>
    /// A moment of contact in an effect: when it lands and how hard. Use it to time your own hit-stop,
    /// camera shake and knockback. Times are seconds from the effect's first frame.
    /// </summary>
    [Serializable]
    public struct PixelFxHit
    {
        [Tooltip("Seconds from the effect's first frame.")]
        public float time;

        [Tooltip("How long to freeze everything on contact, in seconds (hit-stop).")]
        public float freeze;

        [Tooltip("How long to shake the picture, in seconds.")]
        public float shake;

        [Tooltip("How far to shake the picture, in effect pixels.")]
        public float amplitude;

        [Tooltip("How hard the target is pushed back (0 when the hit moves nobody).")]
        public float push;
    }

    /// <summary>
    /// One baked pixel art effect: its frames, how fast they play, and the timing of the move around it.
    /// Play it with a <see cref="PixelFxPlayer"/> or <see cref="PixelFx.Spawn(PixelFxAsset, Vector3, Transform)"/>.
    /// </summary>
    public sealed class PixelFxAsset : ScriptableObject
    {
        [Tooltip("\"category/name\", the effect's id in pixelfx.")]
        public string id;

        public string displayName;

        [TextArea(2, 6)]
        public string description;

        public string category;

        public string[] tags = Array.Empty<string>();

        [Tooltip("The frames in play order. A frame may appear more than once.")]
        public Sprite[] frames = Array.Empty<Sprite>();

        [Min(1f)]
        public float frameRate = 30f;

        [Tooltip("A loop tiles with no seam. A one-shot plays through and ends.")]
        public bool loop;

        [Tooltip("The effect's canvas, in pixels.")]
        public Vector2Int size;

        [Tooltip("The effect's origin on its canvas, in pixels from the top-left corner. Every frame's pivot sits here: the ground under a fire, the point of impact of a hit.")]
        public Vector2Int origin;

        [Tooltip("A 16:9 effect made to fill the screen: weather, transitions, impact frames.")]
        public bool fullScreen;

        [Tooltip("The frame that shows the effect best. Used for thumbnails and in the editor.")]
        public int previewFrame;

        [Tooltip("The material to draw the frames with. The sheets are premultiplied with the glow baked in, so they need PixelFX/Sprite or another premultiplied shader.")]
        public Material material;

        [Header("Acting")]
        [Tooltip("The effect is part of a move a character acts out. The timings below say when.")]
        public bool acted;

        [Tooltip("How long before the first frame the character starts winding up, in seconds.")]
        public float windup;

        [Tooltip("How long after the last frame the character takes to recover, in seconds.")]
        public float recovery;

        [Tooltip("The effect is drawn on the character's body (frost, poison, a scan line) and follows it.")]
        public bool onBody;

        [Tooltip("The moments of contact, in order.")]
        public PixelFxHit[] hits = Array.Empty<PixelFxHit>();

        /// <summary>How many frames it has.</summary>
        public int FrameCount => frames != null ? frames.Length : 0;

        /// <summary>How long one play-through takes, in seconds.</summary>
        public float Duration => frameRate > 0f ? FrameCount / frameRate : 0f;

        /// <summary>Pixels per world unit, read from the frames (the importer bakes it into the sprites).</summary>
        public float PixelsPerUnit
        {
            get
            {
                if (frames != null)
                    for (int i = 0; i < frames.Length; i++)
                        if (frames[i] != null)
                            return frames[i].pixelsPerUnit;
                return 16f;
            }
        }

        /// <summary>The canvas in local space, around the origin, in world units at scale 1.</summary>
        public Bounds LocalBounds
        {
            get
            {
                float ppu = PixelsPerUnit;
                var min = new Vector2(-origin.x, origin.y - size.y) / ppu;
                var max = new Vector2(size.x - origin.x, origin.y) / ppu;
                return new Bounds((min + max) * 0.5f, max - min);
            }
        }

        /// <summary>The frame shown at a time, in seconds. Loops wrap, one-shots hold their last frame.</summary>
        public int FrameAt(float time, bool looping)
        {
            int n = FrameCount;
            if (n == 0)
                return -1;
            int i = Mathf.FloorToInt(time * frameRate + 1e-4f);
            if (looping)
                return ((i % n) + n) % n;
            return Mathf.Clamp(i, 0, n - 1);
        }

        /// <summary>The sprite of a frame, or null when there is none.</summary>
        public Sprite GetFrame(int index)
        {
            return frames != null && index >= 0 && index < frames.Length ? frames[index] : null;
        }

        /// <summary>The sprite that shows the effect best.</summary>
        public Sprite PreviewSprite => GetFrame(Mathf.Clamp(previewFrame, 0, Mathf.Max(0, FrameCount - 1)));
    }
}
