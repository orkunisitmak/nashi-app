// Preserve a batch during drag/keyboard actions, but a normal click singles out its target.
export function nextFileSelection(selected,id,{toggle=false,preserveBatch=false}={}) {
  if (toggle) return selected.includes(id)?selected.filter(x=>x!==id):[...selected,id];
  return preserveBatch&&selected.includes(id)?selected:[id];
}
