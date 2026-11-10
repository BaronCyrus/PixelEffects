#!/usr/bin/env node
// Bakes patch-gg/pixelfx effects into packed sprite sheets for the Unity package.
//
//   node bake.mjs <pixelfx-checkout> <out-dir> [id|category ...] [--page 2048] [--fps N] [--preview <dir>]
//   bun  bake.mjs ../pixelfx ../Effects
//
// For every effect it writes <out>/<category>/<name>.png (and <name>.2.png, ... when one page is not enough)
// and, when baking everything, <out>/index.json: the metadata the Unity importer (Tools > PixelFX > Rebuild
// Effects) turns into sliced sprites, PixelFxAsset objects and prefabs.
//
// Two things make these sheets different from pixelfx's own `tools/render.mjs --sheet` output:
//
// 1. They are premultiplied, with the glow baked in. pixelfx keeps two buffers per frame: `color`
//    (alpha-blended pixels) and `light` (additive glow). A sheet stores
//
//        rgb = color.rgb * color.a + light.rgb        a = color.a
//
//    so one premultiplied blend (Blend One OneMinusSrcAlpha) reproduces "color over the background, then
//    light added on top" exactly. That is what the PixelFX/Sprite shader does.
//
// 2. They are packed. Every frame is trimmed to its content, identical frames are stored once, and the
//    pieces are packed into pages no larger than --page pixels a side. Each sprite remembers where its
//    piece sits on the effect's canvas, so the importer can give it the right pivot.
//
// Only pixelfx's dependency-free `src/` is imported. Needs Node 18+ or Bun.

import { readdirSync, statSync, existsSync, mkdirSync, writeFileSync, readFileSync } from "node:fs";
import { join, resolve, dirname } from "node:path";
import { pathToFileURL } from "node:url";
import { deflateSync } from "node:zlib";
import { createHash } from "node:crypto";

const argv = process.argv.slice(2), pos = [], flags = {};
for (let i = 0; i < argv.length; i++) {
  if (argv[i].startsWith("--")) flags[argv[i].slice(2)] = argv[++i];
  else pos.push(argv[i]);
}
if (pos.length < 2) {
  console.error("usage: bake.mjs <pixelfx-checkout> <out-dir> [id|category ...] [--page 2048] [--fps N] [--preview <dir>]");
  process.exit(1);
}
const SRC = resolve(pos[0]), OUT = resolve(pos[1]), only = pos.slice(2);
const PAGE = +(flags.page ?? 2048), GAP = 1, FPS = flags.fps ? +flags.fps : 0;
const FX_DIR = join(SRC, "src", "fx");
const url = (p) => pathToFileURL(p).href;

const core = await import(url(join(SRC, "src", "core", "index.js")));
const { categories } = await import(url(join(FX_DIR, "categories.js")));
const { createFrame, clearFrame, createFx, actTiming } = core;

// ---- which effects

function allIds() {
  const out = [];
  for (const cat of readdirSync(FX_DIR).sort()) {
    const d = join(FX_DIR, cat);
    if (cat.startsWith("_") || !statSync(d).isDirectory()) continue;
    for (const f of readdirSync(d).sort()) if (f.endsWith(".js") && !f.startsWith("_")) out.push(`${cat}/${f.slice(0, -3)}`);
  }
  return out;
}
const everything = allIds();
const ids = only.length ? everything.filter((id) => only.some((p) => id === p || id.startsWith(p + "/"))) : everything;
if (!ids.length) {
  console.error("no effect matches " + only.join(" "));
  process.exit(1);
}

// ---- rendering (the same steps as pixelfx's tools/lib/fxlib.mjs renderFrames: the effect alone, no backdrop)

/** Full-screen effects (transitions, weather, impact frames): 16:9 and at least 320 wide. */
const isScreen = (meta) => meta.size[0] >= 320 && Math.abs(meta.size[0] / meta.size[1] - 16 / 9) < 0.05;

/**
 * A glow cut off by the canvas edge shows as a box once a frame sits in a game: ease the light out over the
 * last E pixels. Adapted from pixelfx (MIT, Copyright (c) 2026 Patch).
 */
function edgeFade(f, E = 8) {
  const { w, h, light: L } = f;
  for (let y = 0; y < h; y++) {
    const ey = Math.min(y, h - 1 - y);
    for (let x = 0; x < w; x++) {
      const e = Math.min(ey, x, w - 1 - x);
      if (e >= E) { x = w - 1 - E; continue; }
      const i = y * w + x, l = L[i];
      if (!(l & 0xffffff)) continue;
      const u = (e + 0.5) / E, k = u * u * (3 - 2 * u);
      L[i] = (l & 0xff000000) | Math.round((l & 255) * k) | (Math.round(((l >>> 8) & 255) * k) << 8) | (Math.round(((l >>> 16) & 255) * k) << 16);
    }
  }
}

/**
 * Render every frame, trim it, and keep each distinct picture once.
 * Returns { w, h, n, images: [{ w, h, data }], sprites: [{ image, tx, ty }], frames: [sprite index], preview }.
 * A sprite is a picture at a place on the canvas (tx, ty: its top-left corner, y down).
 */
function renderEffect(mod, meta, fps) {
  const [w, h] = meta.size, [ox, oy] = meta.origin;
  const n = Math.max(1, Math.round(meta.duration * fps) + (meta.loop ? 0 : 1));
  const frame = createFrame(w, h), inst = createFx(mod, { x: ox, y: oy }), fade = !isScreen(meta);
  const full = new Uint8Array(w * h * 4);
  const images = [], imageOf = new Map(), sprites = [], spriteOf = new Map(), frames = [];
  let preview = 0, most = -1;
  for (let i = 0; i < n; i++) {
    if (i > 0) inst.update(1 / fps);
    clearFrame(frame);
    inst.draw(frame);
    if (fade) edgeFade(frame);
    full.fill(0);
    const C = frame.color, L = frame.light;
    let x0 = w, y0 = h, x1 = -1, y1 = -1, weight = 0;
    for (let y = 0; y < h; y++)
      for (let x = 0; x < w; x++) {
        const s = y * w + x, c = C[s], l = L[s] & 0xffffff, a = c >>> 24;
        if (!a && !l) continue;
        const r = Math.min(255, Math.round(((c & 255) * a) / 255) + (l & 255));
        const g = Math.min(255, Math.round((((c >>> 8) & 255) * a) / 255) + ((l >>> 8) & 255));
        const b = Math.min(255, Math.round((((c >>> 16) & 255) * a) / 255) + ((l >>> 16) & 255));
        if (!(r | g | b | a)) continue;
        const d = s * 4;
        full[d] = r; full[d + 1] = g; full[d + 2] = b; full[d + 3] = a;
        weight += Math.max(r, g, b, a);
        if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
      }
    let tw = 1, th = 1, tx = Math.max(0, Math.min(w - 1, ox)), ty = Math.max(0, Math.min(h - 1, oy)), data;
    if (x1 < 0) data = new Uint8Array(4); // an empty frame: one clear pixel at the origin
    else {
      tx = x0; ty = y0; tw = x1 - x0 + 1; th = y1 - y0 + 1;
      data = new Uint8Array(tw * th * 4);
      for (let y = 0; y < th; y++) data.set(full.subarray(((ty + y) * w + tx) * 4, ((ty + y) * w + tx + tw) * 4), y * tw * 4);
    }
    const ik = `${tw}x${th}:${createHash("md5").update(data).digest("hex")}`;
    let image = imageOf.get(ik);
    if (image === undefined) { image = images.length; images.push({ w: tw, h: th, data }); imageOf.set(ik, image); }
    const sk = `${image}@${tx},${ty}`;
    let sprite = spriteOf.get(sk);
    if (sprite === undefined) { sprite = sprites.length; sprites.push({ image, tx, ty }); spriteOf.set(sk, sprite); }
    frames.push(sprite);
    if (weight > most) { most = weight; preview = i; }
  }
  return { w, h, n, images, sprites, frames, preview, empty: most <= 0 };
}

// ---- packing: shelves, tallest first, into pages of at most P x P

function pack(images, P, G) {
  const order = images.map((_, i) => i).sort((a, b) => images[b].h - images[a].h || images[b].w - images[a].w);
  const area = images.reduce((s, m) => s + (m.w + G) * (m.h + G), 0), maxW = Math.max(...images.map((m) => m.w));
  const layout = (W) => {
    const pages = [];
    let page, x, y, shelf;
    const open = () => { pages.push((page = { w: 0, h: 0 })); x = 0; y = 0; shelf = 0; };
    open();
    for (const i of order) {
      const m = images[i];
      if (x > 0 && x + m.w > W) { y += shelf + G; x = 0; shelf = 0; }
      if (y > 0 && y + m.h > P) open();
      m.page = pages.length - 1; m.x = x; m.y = y;
      x += m.w + G;
      shelf = Math.max(shelf, m.h);
      page.w = Math.max(page.w, m.x + m.w);
      page.h = Math.max(page.h, m.y + m.h);
    }
    return pages;
  };
  // as square as the content allows; widen until it fits one page, else fill pages at full width
  let W = Math.min(P, Math.max(maxW, Math.ceil(Math.sqrt(area) * 1.02))), pages = layout(W);
  while (pages.length > 1 && W < P) {
    W = Math.min(P, Math.ceil(W * 1.1));
    pages = layout(W);
  }
  for (const page of pages) page.data = new Uint8Array(page.w * page.h * 4);
  for (const m of images) {
    const page = pages[m.page];
    for (let y = 0; y < m.h; y++) page.data.set(m.data.subarray(y * m.w * 4, (y + 1) * m.w * 4), ((m.y + y) * page.w + m.x) * 4);
  }
  return pages;
}

// ---- PNG (RGBA8, adaptive filtering)

const CRC = new Int32Array(256);
for (let n = 0; n < 256; n++) {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  CRC[n] = c;
}
function crc32(buf) {
  let c = -1;
  for (let i = 0; i < buf.length; i++) c = CRC[(c ^ buf[i]) & 255] ^ (c >>> 8);
  return (c ^ -1) >>> 0;
}
function chunk(type, data) {
  const len = Buffer.alloc(4), body = Buffer.concat([Buffer.from(type, "ascii"), data]), crc = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}
function encodePNG(w, h, rgba) {
  const stride = w * 4, raw = Buffer.alloc((stride + 1) * h), zero = new Uint8Array(stride);
  const tries = [0, 1, 2, 3, 4].map(() => new Uint8Array(stride));
  for (let y = 0; y < h; y++) {
    const cur = rgba.subarray(y * stride, (y + 1) * stride), up = y ? rgba.subarray((y - 1) * stride, y * stride) : zero;
    let best = 0, bestSum = Infinity;
    for (let f = 0; f < 5; f++) {
      const t = tries[f];
      let sum = 0;
      for (let x = 0; x < stride; x++) {
        const a = x >= 4 ? cur[x - 4] : 0, b = up[x], c = x >= 4 ? up[x - 4] : 0;
        let v;
        if (f === 0) v = cur[x];
        else if (f === 1) v = cur[x] - a;
        else if (f === 2) v = cur[x] - b;
        else if (f === 3) v = cur[x] - ((a + b) >> 1);
        else {
          const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
          v = cur[x] - (pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
        }
        v &= 255;
        t[x] = v;
        sum += v < 128 ? v : 256 - v;
      }
      if (sum < bestSum) { bestSum = sum; best = f; }
    }
    const o = y * (stride + 1);
    raw[o] = best;
    raw.set(tries[best], o + 1);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    chunk("IHDR", ihdr),
    chunk("IDAT", deflateSync(raw, { level: 9 })),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

/**
 * A strip of up to `max` frames put back together from the pages, over a flat dark backdrop: what the
 * effect looks like in a game. For checking a bake (and the packing) by eye.
 */
function previewStrip(r, pages, max = 8, bg = [21, 19, 29]) {
  const k = Math.min(max, r.n), W = k * (r.w + 1) + 1, H = r.h + 2, out = new Uint8Array(W * H * 4);
  for (let i = 0; i < W * H; i++) out.set([52, 46, 70, 255], i * 4);
  for (let j = 0; j < k; j++) {
    const fi = k === 1 ? 0 : Math.round((j * (r.n - 1)) / (k - 1)), s = r.sprites[r.frames[fi]], m = r.images[s.image], page = pages[m.page];
    const X = 1 + j * (r.w + 1);
    for (let y = 0; y < r.h; y++) for (let x = 0; x < r.w; x++) out.set([...bg, 255], ((y + 1) * W + X + x) * 4);
    for (let y = 0; y < m.h; y++)
      for (let x = 0; x < m.w; x++) {
        const p = ((m.y + y) * page.w + m.x + x) * 4, d = ((s.ty + y + 1) * W + X + s.tx + x) * 4, t = 1 - page.data[p + 3] / 255;
        for (let c = 0; c < 3; c++) out[d + c] = Math.min(255, page.data[p + c] + bg[c] * t);
      }
  }
  return encodePNG(W, H, out);
}

function save(file, data) {
  mkdirSync(dirname(file), { recursive: true });
  writeFileSync(file, data);
}

// ---- bake

const round = (v) => Math.round(v * 1e4) / 1e4;
const effects = [];
let bytes = 0, failed = 0, pageCount = 0, packedPx = 0, fullPx = 0;
const t0 = Date.now();
for (const id of ids) {
  try {
    const mod = await import(url(join(FX_DIR, id + ".js")));
    if (!mod.meta) throw new Error("no meta export");
    if (!mod.draw && !mod.create) throw new Error("exports neither draw nor create");
    const meta = { fps: 30, bg: "dark", loop: false, duration: 1, tags: [], options: {}, ...mod.meta };
    const fps = FPS || meta.fps, [category, slug] = id.split("/");
    const r = renderEffect({ ...mod, meta }, meta, fps), pages = pack(r.images, PAGE, GAP);
    const files = pages.map((page, i) => {
      const file = i ? `${slug}.${i + 1}.png` : `${slug}.png`, png = encodePNG(page.w, page.h, page.data);
      save(join(OUT, category, file), png);
      bytes += png.length;
      packedPx += page.w * page.h;
      return { file, width: page.w, height: page.h };
    });
    pageCount += pages.length;
    fullPx += r.w * r.h * r.n;
    if (flags.preview) save(join(resolve(flags.preview), id.replace("/", "-") + ".png"), previewStrip(r, pages));
    const act = mod.act ? actTiming(mod) : null;
    effects.push({
      id, category, slug,
      name: meta.name ?? slug,
      desc: meta.desc ?? "",
      tags: meta.tags ?? [],
      loop: !!meta.loop,
      fps,
      duration: meta.duration,
      width: r.w, height: r.h,
      pivotX: meta.origin[0], pivotY: meta.origin[1],
      screen: isScreen(meta),
      actor: meta.actor ? (meta.actor === 2 ? 2 : 1) : 0,
      preview: r.preview,
      pages: files,
      // 7 numbers a sprite: page, then its rectangle on the page (x, y, w, h), then its top-left corner on the canvas (x, y). y runs down.
      sprites: r.sprites.flatMap((s) => { const m = r.images[s.image]; return [m.page, m.x, m.y, m.w, m.h, s.tx, s.ty]; }),
      frames: r.frames,
      hasAct: !!act,
      windup: act ? round(act.windup) : 0,
      recovery: act ? round(act.recovery) : 0,
      onBody: act ? act.onBody : false,
      hits: act ? act.hits.map((h) => ({ t: round(h.t), freeze: round(h.freeze), shake: round(h.shake), amp: round(h.amp), push: round(h.push) })) : [],
    });
    if (r.empty) console.error(`\n${id}: every frame is empty`);
    process.stdout.write(".");
  } catch (e) {
    failed++;
    console.error(`\n${id}  FAILED: ${e.stack ?? e}`);
  }
}

if (!only.length && !failed) {
  const pkg = JSON.parse(readFileSync(join(SRC, "package.json"), "utf8"));
  let commit = "";
  try {
    const head = readFileSync(join(SRC, ".git", "HEAD"), "utf8").trim();
    if (!head.startsWith("ref: ")) commit = head;
    else if (existsSync(join(SRC, ".git", head.slice(5)))) commit = readFileSync(join(SRC, ".git", head.slice(5)), "utf8").trim();
  } catch {}
  const index = {
    source: "https://github.com/patch-gg/pixelfx",
    version: pkg.version,
    commit,
    categories: categories.filter((c) => effects.some((e) => e.category === c.id)).map((c) => ({ id: c.id, name: c.name, blurb: c.blurb })),
    effects,
  };
  save(join(OUT, "index.json"), JSON.stringify(index) + "\n");
}
const mb = (v) => (v / 1048576).toFixed(1);
console.log(
  `\n${effects.length} effects, ${pageCount} pages, ${mb(bytes)} MB of PNG, ${mb(packedPx * 4)} MB as RGBA32 (${mb(fullPx * 4)} MB untrimmed), ` +
    `${((Date.now() - t0) / 1000).toFixed(1)} s -> ${OUT}${failed ? `  (${failed} failed)` : ""}`,
);
process.exit(failed ? 1 : 0);
