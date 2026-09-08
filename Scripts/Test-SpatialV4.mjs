import {readFileSync} from 'node:fs';import {spawnSync} from 'node:child_process';import {fileURLToPath} from 'node:url';import assert from 'node:assert/strict';
import {validateRequestV4} from '../Shared/pcg-request-v4.mjs';import {validateSchema} from '../Shared/schema-validator.mjs';import {decodeSpatialWorld} from '../Web/public/spatial-v4-world.mjs';
const examples=JSON.parse(readFileSync(new URL('../Shared/Examples/spatial-v4-gallery.json',import.meta.url),'utf8'));
const schema=JSON.parse(readFileSync(new URL('../Shared/Schema/generated-world-v2.schema.json',import.meta.url),'utf8'));
const dll=fileURLToPath(new URL('../Standalone/Llm2Pcg.CoreHost/bin/Debug/net8.0/Llm2Pcg.CoreHost.dll',import.meta.url));
for(const c of examples){
 assert.equal(validateRequestV4(c.request),null);
 const line=JSON.stringify({request:c.request,includeWorld:true})+'\n';
 const p=spawnSync('dotnet',[dll,'--v4-batch'],{input:line+line,encoding:'utf8',windowsHide:true,maxBuffer:32*1024*1024});assert.equal(p.status,0);
 const rows=p.stdout.trim().split('\n').map(JSON.parse);assert.equal(rows.length,2);
 for(const r of rows){assert.equal(r.ok,true);assert.equal(validateSchema(r.world,schema),null);decodeSpatialWorld(r.world,c.request);}
 assert.deepEqual(rows[0].world,rows[1].world);console.log('PASS v4 full replay '+c.id);
}
console.log('PASS '+examples.length+' gallery requests, '+examples.length*2+' generations; no API key required.');
