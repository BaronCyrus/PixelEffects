# Re-baking the effects

`bake.mjs` renders every effect of [pixelfx](https://github.com/patch-gg/pixelfx) into the packed sprite sheets
and the `index.json` that live in `../Effects`. You only need it to bake a newer pixelfx, your own effects, or
the same effects with different settings. It has no dependencies beyond pixelfx's own `src/` folder and runs on
Node 18+ or Bun.

```bash
git clone https://github.com/patch-gg/pixelfx
```

```bash
node bake.mjs ./pixelfx ../Effects
```

Then, in Unity: **Tools > PixelFX > Rebuild Effects**. That slices the sheets and updates the assets and prefabs
in place, so anything that references them keeps working.

A full bake takes a few minutes and writes about 170 MB of PNG.

## Options

| | |
|---|---|
| `fire ice/frost-nova` | Bake only these categories or effects. `index.json` is only written by a full bake, so use this to check a sheet, not to update the package. |
| `--page 2048` | The largest page, in pixels a side. Effects that need more are split over several pages. |
| `--fps 15` | Bake at another frame rate. Halving it roughly halves the memory of the big full-screen loops. |
| `--preview <dir>` | Also write a strip of frames per effect, put back together over a dark backdrop, to check a bake by eye. |

## Baking your own effects elsewhere

Bake into any folder under `Assets/`, then right-click the `index.json` there and choose
**PixelFX > Build Effects From index.json**. To recolor an effect, change its palette in pixelfx
(see pixelfx's `docs/AUTHORING.md`) and bake again.

## What a sheet holds

pixelfx keeps two buffers per frame: `color` (alpha-blended pixels) and `light` (additive glow). A sheet stores
`rgb = color.rgb * color.a + light.rgb` and `a = color.a`, so one premultiplied blend
(`Blend One OneMinusSrcAlpha`) puts the color over the background and adds the light on top. That is why the
sprites need the `PixelFX/Sprite` shader, and why they look wrong in an image viewer.

Every frame is trimmed to its content, identical frames are stored once, and the pieces are packed into pages.
`index.json` records, for each sprite, its rectangle on its page and where its top-left corner sits on the
effect's canvas; the importer turns that into a pivot on the effect's origin.
