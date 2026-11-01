export function filterFormatRows(rows, value) {
  const fold = text => String(text).normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();
  const term = fold(value.trim());
  for (const row of rows) row.hidden = !fold(row.textContent).includes(term);
}
