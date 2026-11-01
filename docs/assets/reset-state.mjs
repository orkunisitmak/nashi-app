export function isCurrentDemoGeneration(state,generation) {
  return (state.generation??0)===generation;
}

export function resetDemoHistory(state,revoke) {
  // Invalidate pending conversions/exports before clearing their results.
  state.generation=(state.generation??0)+1;
  for (const url of state.urls) revoke(url);
  state.urls.clear();
  state.library=[];
  state.shelf=[];
  state.float={open:false,expanded:false,list:false,selected:[]};
}
