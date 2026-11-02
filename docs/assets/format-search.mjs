export const foldSearchText = text => String(text).normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();

export function filterFormatRows(rows, value) {
  const term = foldSearchText(value.trim());
  for (const row of rows) row.hidden = !foldSearchText(row.textContent).includes(term);
}
