#!/usr/bin/env node
// Makes the README's animated GIFs straight from the baked sheets in the package, so they show exactly
// what ships: Packages/com.baroncyrus.pixelfx/Effects/**/*.png, put back together with index.json and
// composited over pixelfx's dark preview backdrop, the way the PixelFX/Sprite shader draws them.
//
//   node Docs/make-gifs.mjs                 every GIF below, into Docs/
//   node Docs/make-gifs.mjs montage         just that one
//   node Docs/make-gifs.mjs --preview dir   also a PNG of one frame of each, to check the layout
//
// No dependencies: it reads PNG and writes GIF itself. Needs Node 18+ or Bun.

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { join, dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { inflateSync, deflateSync } from "node:zlib";

const HERE = dirname(fileURLToPath(import.meta.url));
const EFFECTS = resolve(HERE, "..", "Packages", "com.baroncyrus.pixelfx", "Effects");

// ---- what to make
//
// A cell is one effect, or a list of one-shots that take turns. Loops should divide the GIF's length so the
// GIF itself loops without a jump; the script says so when one doesn't.

const GIFS = {
  // the README's opening picture: a bit of everything
  montage: {
    seconds: 6, delay: 5, cols: 6, cell: [132, 128],
    cells: [
      "fire/campfire", "fire/torch", "water/fountain", "ice/blizzard-vortex", "lightning/plasma-ball", "lightning/spark-arc",
      ["melee/sword-slash", "melee/double-slash", "melee/uppercut", "melee/claw-swipe", "melee/dagger-flurry"],
      ["impacts/blunt-hit", "impacts/hit-spark", "impacts/shield-block", "impacts/bullet-impact", "impacts/hit-flash"],
      ["explosions/cartoon-pow", "spawn/poof", "pickups/collect-burst"],
      ["ui/damage-number", "ui/crit-number", "ui/heart-lost"],
      ["holy/heal", "arcane/blink-in", "arcane/blink-out"],
      ["nature/acid-splash", "nature/root-snare"],
      "water/water-orb", "shadow/necro-skulls", "scifi/tractor-beam", "fire/spirit-flame", "holy/blessing", "status/frozen",
    ],
  },
  // loops: magic, machines, critters, pickups
  loops: {
    seconds: 4.8, delay: 5, cols: 6, cell: [132, 128],
    cells: [
      "arcane/arcane-orb", "scifi/energy-core", "scifi/teleporter-pad", "holy/divine-shield", "shadow/shadow-flames", "nature/leaf-storm",
      "critters/ghost", "critters/bat", "critters/flying-skull", "pickups/gem-spin", "pickups/coin-spin", "pickups/power-star",
    ],
  },
  // the big ones
  big: {
    seconds: 6, delay: 5, cols: 3, cell: [264, 240],
    cells: [
      ["lightning/lightning-strike", "explosions/plasma-explosion"],
      ["arcane/magic-circle-cast", "explosions/implosion"],
      ["holy/holy-pillar", "lightning/thunderclap"],
    ],
  },
  // full-screen effects
  weather: {
    seconds: 12, delay: 8, cols: 2, cell: [320, 180],
    cells: ["weather/aurora", "weather/snow"],
  },
};

const BACKDROP = [21, 19, 29], INK = [140, 134, 160];
const LABEL = 16; // pixels under each cell for the effect's name

// ---- PNG in (8-bit RGBA, as bake.mjs writes it)

function decodePNG(buf) {
  let p = 8, w = 0, h = 0;
  const idat = [];
  while (p < buf.length) {
    const len = buf.readUInt32BE(p), type = buf.toString("ascii", p + 4, p + 8), data = buf.subarray(p + 8, p + 8 + len);
    if (type === "IHDR") {
      w = data.readUInt32BE(0);
      h = data.readUInt32BE(4);
      if (data[8] !== 8 || data[9] !== 6 || data[12] !== 0) throw new Error("expected an 8-bit RGBA PNG, not interlaced");
    } else if (type === "IDAT") idat.push(data);
    else if (type === "IEND") break;
    p += len + 12;
  }
  const raw = inflateSync(Buffer.concat(idat)), stride = w * 4, out = new Uint8Array(stride * h);
  for (let y = 0; y < h; y++) {
    const f = raw[y * (stride + 1)], s = y * (stride + 1) + 1, d = y * stride;
    for (let x = 0; x < stride; x++) {
      const a = x >= 4 ? out[d + x - 4] : 0, b = y ? out[d + x - stride] : 0, c = x >= 4 && y ? out[d + x - stride - 4] : 0;
      let v = raw[s + x];
      if (f === 1) v += a;
      else if (f === 2) v += b;
      else if (f === 3) v += (a + b) >> 1;
      else if (f === 4) {
        const q = a + b - c, pa = Math.abs(q - a), pb = Math.abs(q - b), pc = Math.abs(q - c);
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      out[d + x] = v & 255;
    }
  }
  return { w, h, data: out };
}

// ---- the effects

const index = JSON.parse(readFileSync(join(EFFECTS, "index.json"), "utf8"));
const byId = new Map(index.effects.map((e) => [e.id, e]));

function load(id) {
  const e = byId.get(id);
  if (!e) throw new Error(`no effect ${id}`);
  if (!e.sheets) {
    e.sheets = e.pages.map((p) => decodePNG(readFileSync(join(EFFECTS, e.category, p.file))));
    // the box its content ever touches, on its canvas
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (let k = 0; k < e.sprites.length; k += 7) {
      const w = e.sprites[k + 3], h = e.sprites[k + 4], tx = e.sprites[k + 5], ty = e.sprites[k + 6];
      if (w === 1 && h === 1) continue; // an empty frame's placeholder
      x0 = Math.min(x0, tx); y0 = Math.min(y0, ty); x1 = Math.max(x1, tx + w); y1 = Math.max(y1, ty + h);
    }
    e.box = { x0, y0, w: x1 - x0, h: y1 - y0 };
  }
  return e;
}

/** Draw a frame of an effect into an RGB picture, its canvas's corner at (ox, oy), inside a clip box. */
function drawFrame(out, W, e, frame, ox, oy, cx0, cy0, cx1, cy1) {
  const k = e.frames[frame] * 7, s = e.sprites, page = e.sheets[s[k]];
  const px = s[k + 1], py = s[k + 2], w = s[k + 3], h = s[k + 4], tx = ox + s[k + 5], ty = oy + s[k + 6];
  for (let j = 0; j < h; j++) {
    const y = ty + j;
    if (y < cy0 || y >= cy1) continue;
    for (let i = 0; i < w; i++) {
      const x = tx + i;
      if (x < cx0 || x >= cx1) continue;
      const sp = ((py + j) * page.w + px + i) * 4, a = page.data[sp + 3], r = page.data[sp], g = page.data[sp + 1], b = page.data[sp + 2];
      if (!(a | r | g | b)) continue;
      // premultiplied color, glow included, over what's there
      const d = (y * W + x) * 3, t = 255 - a;
      out[d] = Math.min(255, r + Math.round((out[d] * t) / 255));
      out[d + 1] = Math.min(255, g + Math.round((out[d + 1] * t) / 255));
      out[d + 2] = Math.min(255, b + Math.round((out[d + 2] * t) / 255));
    }
  }
}

// ---- labels: a 3x5 font, drawn at twice its size

const FONT = {
  A: "010101111101101", B: "110101110101110", C: "011100100100011", D: "110101101101110", E: "111100110100111",
  F: "111100110100100", G: "011100101101011", H: "101101111101101", I: "111010010010111", J: "001001001101010",
  K: "101101110101101", L: "100100100100111", M: "101111111101101", N: "110101101101101", O: "010101101101010",
  P: "110101110100100", Q: "010101101111011", R: "110101110101101", S: "011100010001110", T: "111010010010010",
  U: "101101101101111", V: "101101101101010", W: "101101111111101", X: "101101010101101", Y: "101101010010010",
  Z: "111001010100111", 0: "111101101101111", 1: "010110010010111", 2: "110001010100111", 3: "110001010001110",
  4: "101101111001001", 5: "111100110001110", 6: "011100111101111", 7: "111001010010010", 8: "111101111101111",
  9: "111101111001110", "-": "000000111000000", "&": "010101010101011",
};

function drawLabel(out, W, text, cx, y, maxWidth) {
  let s = text.toUpperCase();
  while (s.length * 8 - 2 > maxWidth) s = s.slice(0, -1);
  let x = Math.round(cx - (s.length * 8 - 2) / 2);
  for (const ch of s) {
    const g = FONT[ch];
    if (g)
      for (let i = 0; i < 15; i++)
        if (g[i] === "1")
          for (let dy = 0; dy < 2; dy++)
            for (let dx = 0; dx < 2; dx++) out.set(INK, ((y + Math.floor(i / 3) * 2 + dy) * W + x + (i % 3) * 2 + dx) * 3);
    x += 8;
  }
}

// ---- a GIF's frames

function renderFrames(name, spec) {
  const [cw, ch] = spec.cell, cols = spec.cols, rows = Math.ceil(spec.cells.length / cols);
  const W = cols * cw, H = rows * (ch + LABEL), n = Math.round((spec.seconds * 100) / spec.delay), dt = spec.delay / 100;
  if (Math.abs(n * dt - spec.seconds) > 1e-6) throw new Error(`${name}: ${spec.seconds} s is not a whole number of ${spec.delay} cs frames`);

  // each cell: who plays when. A loop has the whole GIF; one-shots share it, with a rest after each
  const cells = spec.cells.map((c, ci) => {
    const ids = Array.isArray(c) ? c : [c], list = ids.map(load);
    if (list.length === 1 && list[0].loop) {
      const turns = spec.seconds / list[0].duration;
      if (Math.abs(turns - Math.round(turns)) > 1e-6) console.warn(`  ${name}: ${ids[0]} (${list[0].duration} s) doesn't divide ${spec.seconds} s, so it jumps where the GIF loops`);
      return { offset: 0, slots: [{ e: list[0], from: 0, to: spec.seconds }] };
    }
    if (list.some((e) => e.loop)) throw new Error(`${name}: a loop can't share a cell (${ids.join(", ")})`);
    // a lone one-shot plays as many times as fit
    const lone = list.length === 1 ? Math.max(1, Math.floor(spec.seconds / (list[0].frames.length / list[0].fps + 0.25))) : 1;
    const plays = list.length === 1 ? Array(lone).fill(list[0]) : list;
    const busy = plays.reduce((s, e) => s + e.frames.length / e.fps, 0), rest = (spec.seconds - busy) / plays.length;
    if (rest < 0.15) throw new Error(`${name}: ${ids.join(", ")} need ${busy.toFixed(2)} s, too long for ${spec.seconds} s`);
    let t = 0;
    const slots = plays.map((e) => {
      const slot = { e, from: t, to: t + e.frames.length / e.fps + rest };
      t = slot.to;
      return slot;
    });
    return { offset: (ci * 0.83) % spec.seconds, slots }; // cells start at different moments
  });
  for (const c of cells)
    for (const { e } of c.slots)
      if (e.box.w > cw || e.box.h > ch) console.warn(`  ${name}: ${e.id} (${e.box.w}x${e.box.h}) is cropped to ${cw}x${ch}`);

  const frames = [];
  for (let i = 0; i < n; i++) {
    const out = new Uint8Array(W * H * 3);
    for (let p = 0; p < W * H; p++) out.set(BACKDROP, p * 3);
    cells.forEach((c, ci) => {
      const x0 = (ci % cols) * cw, y0 = Math.floor(ci / cols) * (ch + LABEL);
      const t = (i * dt + c.offset) % spec.seconds, slot = c.slots.find((s) => t >= s.from - 1e-9 && t < s.to - 1e-9) ?? c.slots[c.slots.length - 1];
      const e = slot.e, f = Math.floor((t - slot.from) * e.fps + 1e-6), count = e.frames.length;
      // its content's box in the middle of the cell
      const ox = x0 + Math.floor((cw - e.box.w) / 2) - e.box.x0, oy = y0 + Math.floor((ch - e.box.h) / 2) - e.box.y0;
      if (e.loop || f < count) drawFrame(out, W, e, e.loop ? f % count : f, ox, oy, x0, y0, x0 + cw, y0 + ch);
      drawLabel(out, W, e.name, x0 + cw / 2, y0 + ch + 2, cw - 6);
    });
    frames.push(out);
  }
  return { W, H, frames };
}

// ---- 255 colors: median cut over a 15-bit histogram. Index 0 is the backdrop, exactly; 255 means "unchanged".

function quantize(frames) {
  const bin = (r, g, b) => ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
  const count = new Float64Array(32768), sum = new Float64Array(32768 * 3);
  for (const f of frames)
    for (let p = 0; p < f.length; p += 3) {
      const k = bin(f[p], f[p + 1], f[p + 2]);
      count[k]++;
      sum[k * 3] += f[p]; sum[k * 3 + 1] += f[p + 1]; sum[k * 3 + 2] += f[p + 2];
    }
  const bg = bin(...BACKDROP);
  const mean = (k, c) => sum[k * 3 + c] / count[k];
  const box = (bins) => {
    const lo = [255, 255, 255], hi = [0, 0, 0];
    let total = 0;
    for (const k of bins) {
      total += count[k];
      for (let c = 0; c < 3; c++) {
        const v = mean(k, c);
        if (v < lo[c]) lo[c] = v;
        if (v > hi[c]) hi[c] = v;
      }
    }
    const range = hi.map((v, c) => v - lo[c]), axis = range.indexOf(Math.max(...range));
    return { bins, total, axis, range: range[axis] };
  };
  const all = [];
  for (let k = 0; k < 32768; k++) if (count[k] && k !== bg) all.push(k);
  const boxes = all.length ? [box(all)] : [];
  while (boxes.length && boxes.length < 254) {
    // split the box that spreads the most, weighing in how many pixels it holds
    let best = -1, score = 0;
    boxes.forEach((b, i) => {
      const s = b.bins.length > 1 ? b.range * Math.sqrt(b.total) : 0;
      if (s > score) { score = s; best = i; }
    });
    if (best < 0) break;
    const b = boxes[best];
    b.bins.sort((p, q) => mean(p, b.axis) - mean(q, b.axis));
    let half = 0, cut = 0;
    while (cut < b.bins.length - 1 && half + count[b.bins[cut]] <= b.total / 2) half += count[b.bins[cut++]];
    cut = Math.max(1, cut);
    boxes.splice(best, 1, box(b.bins.slice(0, cut)), box(b.bins.slice(cut)));
  }
  const palette = [BACKDROP];
  for (const b of boxes) {
    const c = [0, 0, 0];
    for (const k of b.bins) for (let j = 0; j < 3; j++) c[j] += sum[k * 3 + j];
    palette.push(c.map((v) => Math.round(v / b.total)));
  }
  // every bin goes to its nearest color
  const map = new Uint8Array(32768);
  for (let k = 0; k < 32768; k++) {
    if (!count[k] || k === bg) continue;
    let best = 0, near = Infinity;
    for (let i = 0; i < palette.length; i++) {
      const dr = mean(k, 0) - palette[i][0], dg = mean(k, 1) - palette[i][1], db = mean(k, 2) - palette[i][2], d = dr * dr * 2 + dg * dg * 4 + db * db;
      if (d < near) { near = d; best = i; }
    }
    map[k] = best;
  }
  const indexed = frames.map((f) => {
    const out = new Uint8Array(f.length / 3);
    for (let p = 0, i = 0; p < f.length; p += 3, i++) out[i] = map[bin(f[p], f[p + 1], f[p + 2])];
    return out;
  });
  return { palette, indexed };
}

// ---- GIF out

/** GIF's LZW, 8-bit codes to start with, as a list of bytes. */
function lzw(pixels) {
  const CLEAR = 256, EOI = 257, BITS = 12, TOP = 1 << BITS, HSIZE = 5003, HSHIFT = 4;
  const htab = new Int32Array(HSIZE).fill(-1), codetab = new Int32Array(HSIZE);
  let out = new Uint8Array(Math.max(1024, pixels.length >> 1)), len = 0;
  const push = (b) => {
    if (len === out.length) { const grown = new Uint8Array(out.length * 2); grown.set(out); out = grown; }
    out[len++] = b;
  };
  let nBits = 9, maxCode = (1 << nBits) - 1, free = CLEAR + 2, cleared = false, acc = 0, accBits = 0;
  const emit = (code) => {
    acc |= code << accBits;
    accBits += nBits;
    while (accBits >= 8) { push(acc & 255); acc >>>= 8; accBits -= 8; }
    // the code size grows when the table outgrows it, and starts over after a clear
    if (free > maxCode || cleared) {
      if (cleared) { nBits = 9; maxCode = (1 << nBits) - 1; cleared = false; }
      else { nBits++; maxCode = nBits === BITS ? TOP : (1 << nBits) - 1; }
    }
    if (code === EOI) while (accBits > 0) { push(acc & 255); acc >>>= 8; accBits -= 8; }
  };
  emit(CLEAR);
  let ent = pixels[0];
  next: for (let p = 1; p < pixels.length; p++) {
    const c = pixels[p], fcode = (c << BITS) + ent;
    let i = (c << HSHIFT) ^ ent;
    if (htab[i] === fcode) { ent = codetab[i]; continue; }
    if (htab[i] >= 0) {
      const step = i === 0 ? 1 : HSIZE - i;
      do {
        if ((i -= step) < 0) i += HSIZE;
        if (htab[i] === fcode) { ent = codetab[i]; continue next; }
      } while (htab[i] >= 0);
    }
    emit(ent);
    ent = c;
    if (free < TOP) { codetab[i] = free++; htab[i] = fcode; }
    else { htab.fill(-1); free = CLEAR + 2; cleared = true; emit(CLEAR); }
  }
  emit(ent);
  emit(EOI);
  return out.subarray(0, len);
}

function encodeGIF(W, H, palette, indexed, delay) {
  const TRANSPARENT = 255, parts = [];
  const u16 = (v) => [v & 255, v >> 8];
  const table = new Uint8Array(768);
  palette.forEach((c, i) => table.set(c, i * 3));
  parts.push(Buffer.from("GIF89a", "ascii"), Buffer.from([...u16(W), ...u16(H), 0xf7, 0, 0]), table);
  parts.push(Buffer.from([0x21, 0xff, 0x0b, ...Buffer.from("NETSCAPE2.0", "ascii"), 3, 1, 0, 0, 0])); // loop forever

  // each frame as the box that changed, with what didn't left see-through; a frame with no change adds its time to the last
  const pieces = [];
  let prev = null;
  for (const f of indexed) {
    if (!prev) pieces.push({ x: 0, y: 0, w: W, h: H, pixels: f, delay, clear: false });
    else {
      let x0 = W, y0 = H, x1 = -1, y1 = -1;
      for (let y = 0; y < H; y++)
        for (let x = 0; x < W; x++)
          if (f[y * W + x] !== prev[y * W + x]) {
            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
          }
      if (x1 < 0) pieces[pieces.length - 1].delay += delay;
      else {
        const w = x1 - x0 + 1, h = y1 - y0 + 1, pixels = new Uint8Array(w * h);
        for (let y = 0; y < h; y++)
          for (let x = 0; x < w; x++) {
            const p = (y0 + y) * W + x0 + x;
            pixels[y * w + x] = f[p] === prev[p] ? TRANSPARENT : f[p];
          }
        pieces.push({ x: x0, y: y0, w, h, pixels, delay, clear: true });
      }
    }
    prev = f;
  }
  for (const p of pieces) {
    parts.push(Buffer.from([0x21, 0xf9, 4, 0x04 | (p.clear ? 1 : 0), ...u16(p.delay), TRANSPARENT, 0])); // keep what's drawn
    parts.push(Buffer.from([0x2c, ...u16(p.x), ...u16(p.y), ...u16(p.w), ...u16(p.h), 0, 8]));
    const data = lzw(p.pixels);
    for (let o = 0; o < data.length; o += 255) {
      const n = Math.min(255, data.length - o);
      parts.push(Buffer.from([n]), Buffer.from(data.buffer, data.byteOffset + o, n));
    }
    parts.push(Buffer.from([0]));
  }
  parts.push(Buffer.from([0x3b]));
  return Buffer.concat(parts);
}

// ---- a PNG of one frame, for --preview

function encodePNG(w, h, rgb) {
  const crcTable = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    crcTable[n] = c;
  }
  const chunk = (type, data) => {
    const body = Buffer.concat([Buffer.from(type, "ascii"), data]), head = Buffer.alloc(4), tail = Buffer.alloc(4);
    let c = -1;
    for (const b of body) c = crcTable[(c ^ b) & 255] ^ (c >>> 8);
    head.writeUInt32BE(data.length);
    tail.writeUInt32BE((c ^ -1) >>> 0);
    return Buffer.concat([head, body, tail]);
  };
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) raw.set(rgb.subarray(y * w * 3, (y + 1) * w * 3), y * (w * 3 + 1) + 1);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(w, 0);
  ihdr.writeUInt32BE(h, 4);
  ihdr[8] = 8;
  ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]);
}

// ---- go

const args = process.argv.slice(2), previewAt = args.indexOf("--preview");
const previewDir = previewAt >= 0 ? resolve(args.splice(previewAt, 2)[1]) : null;
const names = args.length ? args : Object.keys(GIFS);
for (const name of names) {
  const spec = GIFS[name];
  if (!spec) throw new Error(`no GIF called ${name} (there is ${Object.keys(GIFS).join(", ")})`);
  const t0 = Date.now(), { W, H, frames } = renderFrames(name, spec), { palette, indexed } = quantize(frames);
  const gif = encodeGIF(W, H, palette, indexed, spec.delay), file = join(HERE, name + ".gif");
  writeFileSync(file, gif);
  if (previewDir) {
    mkdirSync(previewDir, { recursive: true });
    // the quantized picture, so the preview shows the GIF's own colors
    const k = Math.floor(frames.length * 0.4), rgb = new Uint8Array(W * H * 3);
    indexed[k].forEach((c, i) => rgb.set(palette[c], i * 3));
    writeFileSync(join(previewDir, name + ".png"), encodePNG(W, H, rgb));
  }
  console.log(`${name}.gif  ${W}x${H}  ${frames.length} frames  ${palette.length} colors  ${(gif.length / 1048576).toFixed(1)} MB  ${((Date.now() - t0) / 1000).toFixed(1)} s`);
}
