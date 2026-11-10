# PixelFX for Unity

244 pixel art effects (fire, explosions, magic, weather, UI, pickups, critters and more) as sliced sprites, a
`PixelFxAsset` and a prefab each, with a player component, a glow-aware shader and an editor gallery.

An unofficial Unity port of [patch-gg/pixelfx](https://github.com/patch-gg/pixelfx) by Patch (MIT). Every frame is
baked from pixelfx 0.3.1's own code. Full documentation (中文 / English):
https://github.com/BaronCyrus/PixelEffects#readme

## Use

- **Window > PixelFX Gallery**: browse, search, and drag effects into a scene. Double-click adds one where the
  Scene view is looking.
- Prefabs live next to the assets: `Effects/<category>/<name>.prefab`.
- From code:

```csharp
using PixelEffects;

PixelFx.Spawn(explosion, position);                  // a one-shot: pooled, cleans itself up

var fire = PixelFx.Spawn(campfire, position);        // a loop: plays until you end it
fire.FadeOut();

var slash = PixelFx.Spawn(swordSlash, hitPoint);
slash.HitStop = true;                                // freeze on contact, as the effect was authored
slash.Hit += (player, hit) => Shake(hit.shake, hit.amplitude);
```

## Things to know

- The sheets are premultiplied with the glow baked in (`rgb = color × alpha + light`), so they must be drawn with
  the `PixelFX/Sprite` material (`Runtime/Materials/PixelFxSprite.mat`). Prefabs, `PixelFx.Spawn` and
  `PixelFxPlayer` set it for you. With Sprites/Default the glow disappears.
- Directional effects face right; flip with `SpriteRenderer.flipX`. Every frame's pivot is the effect's origin
  (the ground under a fire, the point of impact of a hit).
- Sprites are 16 pixels per unit, point-filtered and uncompressed. Most effects take a few MB of texture memory;
  the full-screen weather loops take 60–80 MB each.
- `Effects/PixelFxLibrary.asset` lists every effect. Don't reference it from a game build: it pulls in all of
  them (about 1.7 GB of textures).
- To re-bake the effects, see `Tools~/README.md`.

## License

MIT. The effects are © 2026 Patch, with parts adapted from Hugo Duprez's Sprite Fusion VFX and Hollow Crown (both
MIT). See `Third Party Notices.md`. Not affiliated with or endorsed by Patch.
