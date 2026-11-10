using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace PixelEffects.Editor
{
    /// <summary>
    /// Turns a bake (Tools~/bake.mjs: packed sheets and an index.json) into Unity assets: sliced sprites with
    /// the right pivots, a <see cref="PixelFxAsset"/> and a prefab for every effect, and a library of them all.
    /// Running it again updates everything in place, so references to the assets survive a re-bake.
    /// </summary>
    public static class PixelFxImporter
    {
        public const string PackageName = "com.baroncyrus.pixelfx";
        public const string PackageRoot = "Packages/" + PackageName;
        public const string PackageEffects = PackageRoot + "/Effects";
        public const string MaterialPath = PackageRoot + "/Runtime/Materials/PixelFxSprite.mat";
        public const string ShaderName = "PixelFX/Sprite";
        public const float DefaultPixelsPerUnit = 16f;

        // ---- index.json, as bake.mjs writes it

#pragma warning disable 0649
        [Serializable]
        sealed class Index
        {
            public string source, version, commit;
            public CategoryEntry[] categories;
            public EffectEntry[] effects;
        }

        [Serializable]
        sealed class CategoryEntry
        {
            public string id, name, blurb;
        }

        [Serializable]
        sealed class PageEntry
        {
            public string file;
            public int width, height;
        }

        [Serializable]
        sealed class HitEntry
        {
            public float t, freeze, shake, amp, push;
        }

        [Serializable]
        sealed class EffectEntry
        {
            public string id, category, slug, name, desc;
            public string[] tags;
            public bool loop;
            public float fps, duration;
            public int width, height, pivotX, pivotY;
            public bool screen;
            public int actor, preview;
            public PageEntry[] pages;
            public int[] sprites; // 7 a sprite: page, x, y, w, h on the page, then x, y on the canvas (y down)
            public int[] frames;
            public bool hasAct;
            public float windup, recovery;
            public bool onBody;
            public HitEntry[] hits;
        }
#pragma warning restore 0649

        const int SpriteStride = 7;

        // ---- menus

        [MenuItem("Tools/PixelFX/Rebuild Effects", priority = 20)]
        static void RebuildPackage()
        {
            Build(PackageEffects);
        }

        [MenuItem("Assets/PixelFX/Build Effects From index.json", true)]
        static bool BuildSelectedValidate()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return !string.IsNullOrEmpty(path) && Path.GetFileName(path) == "index.json";
        }

        [MenuItem("Assets/PixelFX/Build Effects From index.json")]
        static void BuildSelected()
        {
            var path = AssetDatabase.GetAssetPath(Selection.activeObject);
            Build(Path.GetDirectoryName(path).Replace('\\', '/'));
        }

        // ---- paths

        /// <summary>The file on disk behind an asset path, package paths included.</summary>
        public static string PhysicalPath(string assetPath)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (info != null && assetPath.StartsWith(info.assetPath, StringComparison.Ordinal))
                return Path.Combine(info.resolvedPath, assetPath.Substring(info.assetPath.Length).TrimStart('/'));
            return Path.GetFullPath(assetPath);
        }

        /// <summary>True for a sheet of a bake: a PNG in a category folder next to an index.json.</summary>
        public static bool IsBakedSheet(string assetPath)
        {
            if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                return false;
            var category = Path.GetDirectoryName(assetPath);
            var folder = string.IsNullOrEmpty(category) ? null : Path.GetDirectoryName(category);
            if (string.IsNullOrEmpty(folder))
                return false;
            try
            {
                return File.Exists(Path.Combine(PhysicalPath(folder.Replace('\\', '/')), "index.json"));
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---- texture settings

        /// <summary>
        /// How a sheet has to be imported: point-sampled, uncompressed, no mips, and with "Alpha Is Transparency"
        /// off, because glow-only pixels have alpha 0 and color that must survive.
        /// </summary>
        public static void ApplySheetSettings(TextureImporter importer, float pixelsPerUnit, int maxSize)
        {
            var s = new TextureImporterSettings();
            importer.ReadTextureSettings(s);
            s.textureType = TextureImporterType.Sprite;
            s.spriteMode = (int)SpriteImportMode.Multiple;
            s.spritePixelsPerUnit = pixelsPerUnit;
            s.spriteMeshType = SpriteMeshType.FullRect; // a tight mesh would cut the glow off: it has no alpha
            s.spriteExtrude = 0;
            s.spriteGenerateFallbackPhysicsShape = false;
            s.alphaSource = TextureImporterAlphaSource.FromInput;
            s.alphaIsTransparency = false;
            s.sRGBTexture = true;
            s.mipmapEnabled = false;
            s.readable = false;
            s.filterMode = FilterMode.Point;
            s.wrapMode = TextureWrapMode.Clamp;
            s.npotScale = TextureImporterNPOTScale.None;
            importer.SetTextureSettings(s);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
            importer.maxTextureSize = maxSize;
        }

        static int MaxSizeFor(int width, int height)
        {
            int size = 32, need = Mathf.Max(width, height);
            while (size < need && size < 16384)
                size *= 2;
            return size;
        }

        static GUID StableGuid(string key)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                var hex = new StringBuilder(32);
                foreach (var b in hash)
                    hex.Append(b.ToString("x2"));
                return new GUID(hex.ToString());
            }
        }

        static string SpriteName(EffectEntry e, int sprite) => e.slug + "_" + sprite;

        // ---- build

        /// <summary>Build (or update) the assets for the bake in this folder. It must hold an index.json.</summary>
        public static void Build(string folder)
        {
            folder = folder.TrimEnd('/');
            var indexFile = Path.Combine(PhysicalPath(folder), "index.json");
            if (!File.Exists(indexFile))
            {
                Debug.LogError($"PixelFX: no index.json in {folder}. Bake the effects first (see Tools~/bake.mjs in the package).");
                return;
            }
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(folder);
            if (info != null && info.source != UnityEditor.PackageManager.PackageSource.Embedded && info.source != UnityEditor.PackageManager.PackageSource.Local)
            {
                Debug.LogError($"PixelFX: {folder} is in a read-only package. Embed the package (copy it into Packages/) to rebuild it, or bake into a folder under Assets/.");
                return;
            }

            var index = JsonUtility.FromJson<Index>(File.ReadAllText(indexFile));
            if (index?.effects == null || index.effects.Length == 0)
            {
                Debug.LogError($"PixelFX: {folder}/index.json lists no effects.");
                return;
            }

            try
            {
                // pick up new sheets; the postprocessor gives them their base settings on the way in
                AssetDatabase.Refresh();

                var library = AssetDatabase.LoadAssetAtPath<PixelFxLibrary>(folder + "/PixelFxLibrary.asset");
                float ppu = library != null && library.pixelsPerUnit >= 1f ? library.pixelsPerUnit : DefaultPixelsPerUnit;
                var material = EnsureMaterial();

                SliceSheets(folder, index, ppu);
                var assets = BuildAssets(folder, index, material);
                BuildPrefabs(folder, index, assets, material);
                BuildLibrary(folder, index, assets, library, ppu);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"PixelFX: built {assets.Count} effects in {folder} (pixelfx {index.version}).");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static Material EnsureMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null)
                return material;
            var shader = Shader.Find(ShaderName);
            if (shader == null)
                throw new InvalidOperationException($"PixelFX: shader {ShaderName} not found.");
            var dir = Path.GetDirectoryName(MaterialPath).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(dir).Replace('\\', '/'), Path.GetFileName(dir));
            material = new Material(shader) { name = "PixelFxSprite", enableInstancing = true };
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        // Every page: import settings, and one sprite per distinct piece with its pivot on the effect's origin.
        static void SliceSheets(string folder, Index index, float ppu)
        {
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < index.effects.Length; i++)
                {
                    var e = index.effects[i];
                    if (EditorUtility.DisplayCancelableProgressBar("PixelFX", "Slicing " + e.id, i / (float)index.effects.Length * 0.5f))
                        throw new OperationCanceledException("PixelFX: build cancelled.");
                    for (int p = 0; p < e.pages.Length; p++)
                    {
                        var page = e.pages[p];
                        var path = $"{folder}/{e.category}/{page.file}";
                        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                        if (importer == null)
                            throw new FileNotFoundException($"PixelFX: sheet missing for {e.id}: {path}");

                        ApplySheetSettings(importer, ppu, MaxSizeFor(page.width, page.height));

                        var rects = new List<SpriteRect>();
                        var names = new List<SpriteNameFileIdPair>();
                        for (int k = 0; k * SpriteStride < e.sprites.Length; k++)
                        {
                            int o = k * SpriteStride;
                            if (e.sprites[o] != p)
                                continue;
                            int x = e.sprites[o + 1], y = e.sprites[o + 2], w = e.sprites[o + 3], h = e.sprites[o + 4];
                            int tx = e.sprites[o + 5], ty = e.sprites[o + 6];
                            var rect = new SpriteRect
                            {
                                name = SpriteName(e, k),
                                spriteID = StableGuid(e.id + "/" + k),
                                rect = new Rect(x, page.height - y - h, w, h), // the bake counts y down, Unity up
                                alignment = SpriteAlignment.Custom,
                                // the effect's origin, measured from the piece's bottom-left corner; it may lie outside the piece
                                pivot = new Vector2((e.pivotX - tx) / (float)w, (ty + h - e.pivotY) / (float)h),
                                border = Vector4.zero,
                            };
                            rects.Add(rect);
                            names.Add(new SpriteNameFileIdPair(rect.name, rect.spriteID));
                        }

                        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
                        provider.InitSpriteEditorDataProvider();
                        provider.SetSpriteRects(rects.ToArray());
                        provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(names);
                        provider.Apply();
                        importer.SaveAndReimport();
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        static Dictionary<string, PixelFxAsset> BuildAssets(string folder, Index index, Material material)
        {
            var assets = new Dictionary<string, PixelFxAsset>();
            var byName = new Dictionary<string, Sprite>();
            for (int i = 0; i < index.effects.Length; i++)
            {
                var e = index.effects[i];
                EditorUtility.DisplayProgressBar("PixelFX", "Building " + e.id, 0.5f + i / (float)index.effects.Length * 0.25f);

                byName.Clear();
                foreach (var page in e.pages)
                    foreach (var o in AssetDatabase.LoadAllAssetRepresentationsAtPath($"{folder}/{e.category}/{page.file}"))
                        if (o is Sprite s)
                            byName[s.name] = s;

                var frames = new Sprite[e.frames.Length];
                for (int f = 0; f < frames.Length; f++)
                    if (!byName.TryGetValue(SpriteName(e, e.frames[f]), out frames[f]))
                        throw new InvalidOperationException($"PixelFX: {e.id} frame {f}: sprite {SpriteName(e, e.frames[f])} was not imported.");

                var path = $"{folder}/{e.category}/{e.slug}.asset";
                var asset = AssetDatabase.LoadAssetAtPath<PixelFxAsset>(path);
                bool fresh = asset == null;
                if (fresh)
                    asset = ScriptableObject.CreateInstance<PixelFxAsset>();

                asset.id = e.id;
                asset.displayName = e.name;
                asset.description = e.desc;
                asset.category = e.category;
                asset.tags = e.tags ?? Array.Empty<string>();
                asset.frames = frames;
                asset.frameRate = e.fps;
                asset.loop = e.loop;
                asset.size = new Vector2Int(e.width, e.height);
                asset.origin = new Vector2Int(e.pivotX, e.pivotY);
                asset.fullScreen = e.screen;
                asset.previewFrame = Mathf.Clamp(e.preview, 0, Mathf.Max(0, frames.Length - 1));
                asset.material = material;
                asset.acted = e.hasAct;
                asset.windup = e.windup;
                asset.recovery = e.recovery;
                asset.onBody = e.onBody;
                var hits = e.hits ?? Array.Empty<HitEntry>();
                asset.hits = new PixelFxHit[hits.Length];
                for (int h = 0; h < hits.Length; h++)
                    asset.hits[h] = new PixelFxHit { time = hits[h].t, freeze = hits[h].freeze, shake = hits[h].shake, amplitude = hits[h].amp, push = hits[h].push };

                if (fresh)
                    AssetDatabase.CreateAsset(asset, path);
                else
                    EditorUtility.SetDirty(asset);
                assets[e.id] = asset;
            }
            AssetDatabase.SaveAssets();
            return assets;
        }

        // A prefab an effect: a SpriteRenderer and a player, ready to drop into a scene or Instantiate.
        static void BuildPrefabs(string folder, Index index, Dictionary<string, PixelFxAsset> assets, Material material)
        {
            for (int i = 0; i < index.effects.Length; i++)
            {
                var e = index.effects[i];
                EditorUtility.DisplayProgressBar("PixelFX", "Prefab " + e.id, 0.75f + i / (float)index.effects.Length * 0.25f);
                var asset = assets[e.id];
                var path = $"{folder}/{e.category}/{e.slug}.prefab";
                bool existing = File.Exists(PhysicalPath(path));
                var root = existing ? PrefabUtility.LoadPrefabContents(path) : new GameObject(e.slug);
                try
                {
                    if (!root.TryGetComponent<SpriteRenderer>(out var renderer))
                        renderer = root.AddComponent<SpriteRenderer>();
                    if (!root.TryGetComponent<PixelFxPlayer>(out var player))
                        player = root.AddComponent<PixelFxPlayer>();

                    renderer.sprite = asset.PreviewSprite;
                    renderer.sharedMaterial = material;

                    var so = new SerializedObject(player);
                    so.FindProperty("effect").objectReferenceValue = asset;
                    if (!existing) // a one-shot dropped in a scene or instantiated cleans itself up; leave later tweaks alone
                        so.FindProperty("whenFinished").enumValueIndex = (int)(e.loop ? PixelFxPlayer.EndAction.Hide : PixelFxPlayer.EndAction.Destroy);
                    so.ApplyModifiedPropertiesWithoutUndo();

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    if (existing)
                        PrefabUtility.UnloadPrefabContents(root);
                    else
                        UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        static void BuildLibrary(string folder, Index index, Dictionary<string, PixelFxAsset> assets, PixelFxLibrary library, float ppu)
        {
            bool fresh = library == null;
            if (fresh)
                library = ScriptableObject.CreateInstance<PixelFxLibrary>();
            library.source = index.source;
            library.sourceVersion = index.version;
            library.sourceCommit = index.commit;
            library.pixelsPerUnit = ppu;

            var categories = new List<PixelFxLibrary.Category>();
            var listed = new HashSet<string>();
            foreach (var c in index.categories ?? Array.Empty<CategoryEntry>())
            {
                categories.Add(new PixelFxLibrary.Category { id = c.id, name = c.name, blurb = c.blurb });
                listed.Add(c.id);
            }
            foreach (var e in index.effects) // a category the index forgot to describe still gets its effects
                if (listed.Add(e.category))
                    categories.Add(new PixelFxLibrary.Category { id = e.category, name = e.category, blurb = "" });
            foreach (var c in categories)
            {
                var list = new List<PixelFxAsset>();
                foreach (var e in index.effects)
                    if (e.category == c.id)
                        list.Add(assets[e.id]);
                c.effects = list.ToArray();
            }
            library.categories = categories.ToArray();

            if (fresh)
                AssetDatabase.CreateAsset(library, folder + "/PixelFxLibrary.asset");
            else
                EditorUtility.SetDirty(library);
        }
    }

    /// <summary>Gives a freshly baked sheet sane settings on its first import, before the importer slices it.</summary>
    sealed class PixelFxSheetPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetImporter.importSettingsMissing || !PixelFxImporter.IsBakedSheet(assetPath))
                return;
            PixelFxImporter.ApplySheetSettings((TextureImporter)assetImporter, PixelFxImporter.DefaultPixelsPerUnit, 8192);
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    PixelFxPreview.Clear();
                    break;
                }
        }
    }
}
