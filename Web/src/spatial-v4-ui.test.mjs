import test from 'node:test';import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';import {runInNewContext} from 'node:vm';
import {parseStrictJson} from '../../Shared/strict-json.mjs';
import {emptyIntent,resolveIntent} from './request-resolver.mjs';

function ui(fetchImplementation) {
 const nodes=new Map(),get=id=>{if(!nodes.has(id))nodes.set(id,{value:'',files:[],textContent:'',disabled:false,classList:{toggle(){}},append(){}});return nodes.get(id);};
 let id=0;
 const source=readFileSync(new URL('../public/spatial-v4.js',import.meta.url),'utf8').replace("import {parseStrictJson} from '/strict-json.mjs';",'');
 runInNewContext(source,{document:{getElementById:get,createElement:()=>({})},window:{addEventListener(){}},parseStrictJson,structuredClone,crypto:{randomUUID:()=>String(++id).padStart(16,'0')},fetch:async(url,options)=>url.endsWith('/examples')?{ok:true,json:async()=>({ok:true,examples:[]})}:fetchImplementation(url,options)});
 return get;
}
const request=resolveIntent({...emptyIntent(),seed:234}).request;
test('loading a saved request clears a failed seed-variation request ID',async()=>{
 const ids=[];const get=ui(async(_url,options)=>{ids.push(JSON.parse(options.body).requestId);throw Error('simulated transport failure');});
 const load=get('load');load.files=[{size:1024,text:async()=>JSON.stringify(request)}];await load.onchange();
 await get('new-seed').onclick();await get('new-seed').onclick();assert.equal(ids[0],ids[1],'transport retry must retain its ID');
 await load.onchange();await get('new-seed').onclick();assert.notEqual(ids[0],ids[2],'different loaded request must not reuse a failed ID');
});
test('invalid saved JSON clears prior request and permits reselecting the same file',async()=>{
 const get=ui(()=>{throw Error('no network expected');}),load=get('load');
 load.files=[{size:1024,text:async()=>JSON.stringify(request)}];await load.onchange();assert.equal(get('generate').disabled,false);
 load.value='selected.json';load.files=[{size:10,text:async()=>'{bad json'}];await load.onchange();
 assert.equal(get('generate').disabled,true);assert.equal(get('request').textContent,'');assert.equal(load.value,'');
});
