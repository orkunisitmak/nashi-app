export const foldSearchText = text => String(text).trim().normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase();

export function filterFormatRows(rows, value) {
  const term = foldSearchText(value);
  for (const row of rows) row.hidden = !foldSearchText(row.textContent).includes(term);
}
