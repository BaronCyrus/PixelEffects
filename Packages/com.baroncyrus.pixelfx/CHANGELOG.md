# Changelog

## [0.1.0] - 2026-11-10

First release.

- 244 effects in 21 categories, baked from pixelfx 0.3.1 (commit `044e12a`): packed sprite sheets, a
  `PixelFxAsset` and a prefab for each, and a `PixelFxLibrary` of them all.
- `PixelFxPlayer`: plays an effect on a SpriteRenderer, with loop modes, speed, unscaled time, hit events,
  optional hit-stop, fade-out, hue shift and intensity.
- `PixelFx.Spawn`: pooled fire-and-forget effects from code.
- `PixelFX/Sprite` shader: premultiplied sprites with the glow baked in.
- Editor: **Window > PixelFX Gallery**, animated previews and thumbnails for effect assets, and
  **Tools > PixelFX > Rebuild Effects** to rebuild everything from a bake.
- `Tools~/bake.mjs`: the script that bakes pixelfx into the sheets.
