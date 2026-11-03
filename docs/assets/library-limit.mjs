// The native Library holds its latest 50 outputs; avoid unbounded demo
// history and release object URLs for exports no longer reachable in the UI.
export function retainDemoResult(state,result,revoke,limit=50) {
  state.library.unshift(result);
  for (const old of state.library.splice(limit)) {
    if (old.url && state.urls.has(old.url)) {
      state.urls.delete(old.url);
      revoke(old.url);
    }
  }
  return state.library.length;
}
