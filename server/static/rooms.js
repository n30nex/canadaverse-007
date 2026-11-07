'use strict';
const rooms = document.getElementById('rooms');
const connection = document.getElementById('connection');
const refresh = document.getElementById('refresh');
let pending = false;
function element(tag, text, className) { const node = document.createElement(tag); node.textContent = text; if (className) node.className = className; return node; }
function empty(title, detail, failed=false) { const box=element('div','','empty'+(failed?' error':'')); box.append(element('strong',title),element('p',detail)); rooms.replaceChildren(box); }
async function loadRooms() {
  if (pending) return; pending=true; refresh.disabled=true;
  const controller=new AbortController(); const timeout=setTimeout(()=>controller.abort(),8000);
  try {
    const response=await fetch('/v1/rooms',{cache:'no-store',signal:controller.signal});
    if (!response.ok) throw new Error('HTTP '+response.status);
    const data=await response.json();
    if (data.schema!==1 || !Array.isArray(data.rooms)) throw new Error('Unsupported directory response');
    rooms.replaceChildren(); document.getElementById('count').textContent=String(data.rooms.length);
    connection.textContent='Connected · Updated '+new Date().toLocaleTimeString();
    if (!data.rooms.length) empty('No rooms right now','Open the Windows launcher to create a staging room. No sample or simulated rooms are shown.');
    for (const room of data.rooms) {
      const row=element('article','','room'); const name=element('div','');
      name.append(element('strong',room.name),element('small','Hosted by '+room.host));
      row.append(name,element('div',room.map),element('small',room.players+' / '+room.capacity+' listed'),element('span','Staging only','pill'));
      rooms.append(row);
    }
  } catch(error) {
    document.getElementById('count').textContent='—';connection.textContent='Master server unavailable';
    empty('Couldn’t reach the directory',error.message+'. Local play in the launcher still works. Try refreshing in a moment.',true);
  } finally {clearTimeout(timeout);pending=false;refresh.disabled=false;}
}
refresh.addEventListener('click',loadRooms);loadRooms();setInterval(()=>{if(!document.hidden)loadRooms();},15000);
