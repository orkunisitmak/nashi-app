/** Collage geometry shared by the web demo and its node test. Drawing never throws. */

export function collageLabelText(style, index, start) {
  if (style !== 'numbers' && style !== 'letters') return null;
  const n = Math.min(9999, Math.max(1, Number(start) || 1)) + index;
  if (n < 1 || n > 9999) return null;
  if (style === 'numbers') return String(n);
  let x = n, text = '';
  while (x > 0) { x -= 1; text = String.fromCharCode(65 + (x % 26)) + text; x = Math.floor(x / 26); }
  return text;
}

export function collageGrid(count, layout, columns, width, height, gap) {
  const n = Math.max(0, count | 0);
  const w = Math.max(1, Number(width) || 1);
  const h = Math.max(1, Number(height) || 1);
  const cols = layout === 'column' ? 1 : layout === 'row' ? Math.max(1, n) : Math.max(1, Math.min(Math.max(n, 1), Number(columns) || 2));
  const rows = Math.max(1, Math.ceil((n || 1) / cols));
  const minCell = 8;
  const requested = Math.max(0, Number(gap) || 0);
  const gapLimit = Math.min(requested, Math.max(0, (w - cols * minCell) / (cols + 1)), Math.max(0, (h - rows * minCell) / (rows + 1)));
  const cellW = (w - gapLimit * (cols + 1)) / cols;
  const cellH = (h - gapLimit * (rows + 1)) / rows;
  const cells = [];
  for (let i = 0; i < n; i++) cells.push({ x: gapLimit + (i % cols) * (cellW + gapLimit), y: gapLimit + Math.floor(i / cols) * (cellH + gapLimit), w: cellW, h: cellH });
  return { cols, rows, gap: gapLimit, cellW, cellH, cells };
}

export function collageCanvasSize(count, layout, columns, gap, width = 900) {
  const n = Math.max(1, count | 0);
  const cols = layout === 'column' ? 1 : layout === 'row' ? n : Math.max(1, Math.min(n, Number(columns) || 2));
  const rows = Math.ceil(n / cols);
  const minCell = 8;
  const raw = Math.max(0, Number(gap) || 0);
  const gapW = Math.min(raw, Math.max(0, (width - cols * minCell) / (cols + 1)));
  const cell = (width - gapW * (cols + 1)) / cols;
  return { width, height: Math.max(minCell, Math.round(cell * 0.7 * rows + gapW * (rows + 1))) };
}

function intersection(x, y, w, h, cx, cy, cw, ch) {
  const left = Math.max(x, cx), top = Math.max(y, cy);
  const right = Math.min(x + w, cx + cw), bottom = Math.min(y + h, cy + ch);
  if (right - left < 1 || bottom - top < 1) return null;
  return { x: left, y: top, w: right - left, h: bottom - top };
}

function badgeRect(visible, position) {
  const side = Math.min(visible.w, visible.h);
  const inset = Math.max(4, side * 0.045);
  const room = side - inset * 2;
  if (room <= 4) return null;
  const font = Math.min(Math.max(12, side * 0.22), side * 0.46);
  const diameter = Math.min(room, Math.min(side * 0.9, font * 1.45));
  const left = visible.x + inset, right = visible.x + visible.w - inset - diameter;
  const top = visible.y + inset, bottom = visible.y + visible.h - inset - diameter;
  let x = left, y = top;
  if (position === 'topRight') { x = right; y = top; }
  else if (position === 'bottomLeft') { x = left; y = bottom; }
  else if (position === 'bottomRight') { x = right; y = bottom; }
  else if (position === 'center') { x = visible.x + (visible.w - diameter) / 2; y = visible.y + (visible.h - diameter) / 2; }
  return { x, y, d: diameter };
}

/** Returns the badge rectangles it drew. A broken image cannot abort the preview. */
export function paintCollage(ctx, images, options = {}) {
  const marks = [];
  try {
    const width = ctx.canvas.width, height = ctx.canvas.height;
    ctx.fillStyle = options.background || '#ffffff';
    ctx.fillRect(0, 0, width, height);
    const list = Array.isArray(images) ? images : [];
    const grid = collageGrid(list.length, options.layout || 'row', options.columns, width, height, options.gap);
    list.forEach((im, i) => {
      const cell = grid.cells[i];
      if (!im || !(im.width > 0) || !(im.height > 0) || !(cell.w > 0) || !(cell.h > 0)) return;
      const sx = cell.w / im.width, sy = cell.h / im.height;
      const scale = options.fit === 'fill' ? Math.max(sx, sy) : Math.min(sx, sy);
      const w = im.width * scale, h = im.height * scale;
      const x = cell.x + (cell.w - w) / 2, y = cell.y + (cell.h - h) / 2;
      ctx.save();
      try {
        ctx.beginPath();
        ctx.rect(cell.x, cell.y, cell.w, cell.h);
        ctx.clip();
        ctx.drawImage(im, x, y, w, h);
        const visible = intersection(x, y, w, h, cell.x, cell.y, cell.w, cell.h);
        const text = collageLabelText(options.labels, i, options.labelStart);
        if (text && visible && visible.w > 2 && visible.h > 2) {
          const badge = badgeRect(visible, options.labelPosition || 'topLeft');
          if (badge) {
            marks.push({ index: i, text, x: badge.x, y: badge.y, d: badge.d });
            ctx.beginPath();
            ctx.fillStyle = options.labelBadge || '#1C1C1C';
            ctx.arc(badge.x + badge.d / 2, badge.y + badge.d / 2, badge.d / 2, 0, Math.PI * 2);
            ctx.fill();
            ctx.fillStyle = options.labelColor || '#FFFFFF';
            ctx.font = `700 ${Math.max(8, badge.d * 0.42)}px sans-serif`;
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText(text, badge.x + badge.d / 2, badge.y + badge.d / 2);
          }
        }
      } catch { /* skip one broken frame */ }
      finally { ctx.restore(); }
    });
  } catch { /* leave the caller holding the previous frame */ }
  return marks;
}
