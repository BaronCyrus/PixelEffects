using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelEffects
{
    /// <summary>
    /// Fire-and-forget effects from code. Instances come from a pool and go back to it when they end.
    /// <code>PixelFx.Spawn(explosion, transform.position);</code>
    /// </summary>
    public static class PixelFx
    {
        static readonly Stack<PixelFxPlayer> Pool = new Stack<PixelFxPlayer>();
        static Transform root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Pool.Clear();
            root = null;
        }

        /// <summary>
        /// Play an effect at a place. A one-shot cleans itself up when it ends. A loop plays until you call
        /// <see cref="PixelFxPlayer.Stop"/> or <see cref="PixelFxPlayer.FadeOut"/> on the player you get back.
        /// </summary>
        public static PixelFxPlayer Spawn(PixelFxAsset effect, Vector3 position, Transform parent = null)
        {
            return Spawn(effect, position, Quaternion.identity, parent);
        }

        /// <inheritdoc cref="Spawn(PixelFxAsset, Vector3, Transform)"/>
        public static PixelFxPlayer Spawn(PixelFxAsset effect, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (effect == null)
                throw new ArgumentNullException(nameof(effect));

            PixelFxPlayer player = null;
            while (Pool.Count > 0 && player == null)
                player = Pool.Pop(); // skips any that were destroyed along with a parent or a scene

            if (player == null)
            {
                var go = new GameObject("PixelFX");
                go.SetActive(false);
                go.AddComponent<SpriteRenderer>();
                player = go.AddComponent<PixelFxPlayer>();
                player.PlayOnEnable = false;
                player.recycle = Release;
            }

            // a recycled instance starts from a clean slate
            var renderer = player.Renderer;
            renderer.color = Color.white;
            renderer.flipX = false;
            renderer.flipY = false;
            renderer.sortingLayerID = 0;
            renderer.sortingOrder = 0;
            player.Loop = PixelFxPlayer.LoopMode.Auto;
            player.Speed = 1f;
            player.UnscaledTime = false;
            player.RepeatDelay = 0f;
            player.HitStop = false;
            player.HueShift = 0f;
            player.Intensity = 1f;

            var t = player.transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(position, rotation);
            t.localScale = Vector3.one;
            player.gameObject.SetActive(true);
            player.Play(effect);
            return player;
        }

        static void Release(PixelFxPlayer player)
        {
            if (player == null)
                return;
            if (root == null)
            {
                var go = new GameObject("[PixelFX Pool]");
                UnityEngine.Object.DontDestroyOnLoad(go);
                root = go.transform;
            }
            player.gameObject.SetActive(false);
            player.transform.SetParent(root, false);
            Pool.Push(player);
        }
    }
}
