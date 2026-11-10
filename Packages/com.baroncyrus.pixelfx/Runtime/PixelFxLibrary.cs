using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixelEffects
{
    /// <summary>
    /// Every effect of a bake, by category. Handy for tools, galleries and looking effects up by id.
    /// Careful in a game: referencing the library puts every effect it lists into the build. Reference the
    /// effects you use instead.
    /// </summary>
    public sealed class PixelFxLibrary : ScriptableObject
    {
        [Serializable]
        public sealed class Category
        {
            public string id;
            public string name;
            public string blurb;
            public PixelFxAsset[] effects = Array.Empty<PixelFxAsset>();
        }

        [Tooltip("Where the effects were baked from.")]
        public string source;
        public string sourceVersion;
        public string sourceCommit;

        [Tooltip("Pixels per world unit for the sprites. Change it, then run Tools > PixelFX > Rebuild Effects.")]
        [Min(1f)]
        public float pixelsPerUnit = 16f;

        public Category[] categories = Array.Empty<Category>();

        Dictionary<string, PixelFxAsset> byId;

        /// <summary>How many effects it lists.</summary>
        public int Count
        {
            get
            {
                int n = 0;
                foreach (var c in categories)
                    n += c.effects.Length;
                return n;
            }
        }

        /// <summary>Every effect, category by category.</summary>
        public IEnumerable<PixelFxAsset> Effects
        {
            get
            {
                foreach (var c in categories)
                    foreach (var e in c.effects)
                        if (e != null)
                            yield return e;
            }
        }

        /// <summary>The effect with this id ("fire/campfire"), or null.</summary>
        public PixelFxAsset Find(string id)
        {
            if (byId == null)
            {
                byId = new Dictionary<string, PixelFxAsset>(StringComparer.Ordinal);
                foreach (var e in Effects)
                    byId[e.id] = e;
            }
            return id != null && byId.TryGetValue(id, out var fx) ? fx : null;
        }

        /// <summary>The category with this id ("fire"), or null.</summary>
        public Category FindCategory(string id)
        {
            foreach (var c in categories)
                if (c.id == id)
                    return c;
            return null;
        }

        void OnEnable() => byId = null;
        void OnValidate() => byId = null;
    }
}
