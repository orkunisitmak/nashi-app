'use strict';
const search=document.getElementById('format-search');
if(search){const fold=text=>String(text).normalize('NFD').replace(/[\u0300-\u036f]/g,'').toLowerCase();const rows=[...document.querySelectorAll('#format-table tbody tr')];search.addEventListener('input',()=>{const term=fold(search.value.trim());for(const row of rows)row.hidden=!fold(row.textContent).includes(term);});}
