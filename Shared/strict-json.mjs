// JSON.parse supplies grammar validation; this pass rejects duplicate decoded object keys
// before callers can accidentally treat a last-key-wins object as an unambiguous request.
export function parseStrictJson(text) {
  if(typeof text!=='string'||text.length>65536)throw new Error('JSON size limit');
  const value=JSON.parse(text),stack=[];
  for(let i=0;i<text.length;i++) {
    const c=text[i];
    if(c==='{'||c==='['){stack.push(c==='{'?new Set():null);if(stack.length>16)throw new Error('JSON nesting limit');}
    else if(c==='}'||c===']')stack.pop();
    else if(c==='"') {
      const start=i++;
      while(text[i]!=='"'){if(text[i]==='\\')i++;i++;}
      let next=i+1;while(/\s/.test(text[next]??'') && next<text.length)next++;
      if(text[next]===':') {
        const key=JSON.parse(text.slice(start,i+1)),keys=stack.at(-1);
        if(keys.has(key))throw new Error('Duplicate JSON key: '+key);keys.add(key);
      }
    }
  }
  return value;
}
