# Notices

This repository is an unofficial Unity port of **pixelfx** by Patch. It is not made or endorsed by Patch.

**pixelfx.** Every sprite sheet in `Packages/com.baroncyrus.pixelfx/Effects` is rendered from the effects of
pixelfx 0.3.1 (https://github.com/patch-gg/pixelfx, commit `044e12a`), Copyright (c) 2026 Patch, MIT License.
The effect names, descriptions, tags and hit timings in the assets come from the same source. The bake script
(`Packages/com.baroncyrus.pixelfx/Tools~/bake.mjs`) adapts a few lines of pixelfx's `tools/lib/fxlib.mjs`.

**Hugo Duprez.** pixelfx's simulation core and the effects that say so at the top of their source file adapt
Sprite Fusion's open-source pixel art VFX (https://www.spritefusion.com/pixel-art-vfx),
Copyright (c) 2026 Hugo Duprez, MIT License. Sheets baked from those effects carry that work with them.

**Hollow Crown.** pixelfx's acting (the wind-up, hit-stop and recovery timings exported here as each effect's
`windup`, `recovery` and `hits`) adapts how Hollow Crown plays its skills,
from https://github.com/rehan-remade/hollow-crown, Copyright (c) 2026 rehan-remade, MIT License.

Not included: pixelfx's JavaScript source, its gallery, and the Patch Pixel font (which is Patch's own and not
MIT-licensed).
