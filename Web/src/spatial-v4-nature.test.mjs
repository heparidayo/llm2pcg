import test from 'node:test';import assert from 'node:assert/strict';import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {resolveIntent,emptyIntent} from './request-resolver.mjs';import {analyzePrompt} from './prompt-intent.mjs';
import {biomeVersions,validateRequestV4} from '../../Shared/pcg-request-v4.mjs';import {decodeSpatialWorld} from '../public/spatial-v4-world.mjs';
test('sentence-final punctuation preserves explicit integers but never truncates decimals',()=>{
 for(const [prompt,seed] of [['Seed 902.',902],['시드 -19.',-19],['시드는 107이야.',107]])assert.equal(analyzePrompt(prompt).constraints.find(c=>c.path==='seed')?.value,seed);
 assert.equal(analyzePrompt('64x128.').constraints.find(c=>c.path==='mapHeight')?.value,128);
 for(const prompt of ['seed 12.3','seed -19.7'])assert.equal(analyzePrompt(prompt).constraints.some(c=>c.path==='seed'),false);
});
test('Nature versions are paired, defaults are explicit and canonical types remain shared',()=>{
 for(const worldType of Object.keys(biomeVersions)){const r=resolveIntent({...emptyIntent(),worldType,seed:1}).request;assert.equal(validateRequestV4(r),null);assert.equal(r.generatorVersion,biomeVersions[worldType]);assert.ok(validateRequestV4({...r,generatorVersion:'bad'}));}
 const snow=resolveIntent({...emptyIntent(),worldType:'Snowfield',seed:1,distributionRules:[{category:'waterProps',types:['reeds'],amountMode:'RelativeDensity',amount:500,region:'WholeMap',featureId:null}]}).request;
 assert.ok(snow.distributionRules.find(d=>d.category==='waterProps').maxCount>0);
});
test('Nature default water, explicit None, exclusive lake, mountain and river preserve contracts', {skip:process.env.PCG_V4_INTEGRATION!=='1'},()=>{
 const cases=[];
 for(const worldType of ['Desert','Snowfield','Swamp'])for(const scenario of ['base','none','lake','mountain','river']){
  const d={...emptyIntent(),worldType,seed:234,mapWidth:64,mapHeight:64};if(scenario==='none')d.waterMode='None';
  if(['lake','mountain','river'].includes(scenario))d.spatialFeatures=[{id:'feature',kind:{lake:'Lake',mountain:'Mountain',river:'River'}[scenario],placement:scenario==='river'?'CenterCrossing':'Center',orientation:scenario==='river'?'NorthSouth':null,size:'Medium',exclusive:scenario==='lake'}];
  cases.push({worldType,scenario,request:resolveIntent(d).request});
 }
 const result=spawnSync('dotnet',[fileURLToPath(new URL('../../Standalone/Llm2Pcg.CoreHost/bin/Debug/net8.0/Llm2Pcg.CoreHost.dll',import.meta.url)),'--v4-batch'],{input:cases.map(c=>JSON.stringify({request:c.request,includeWorld:true})).join('\n')+'\n',encoding:'utf8',windowsHide:true,maxBuffer:32*1024*1024});assert.equal(result.status,0,result.stderr);
 const outputs=result.stdout.trim().split(/\r?\n/).map(JSON.parse);
 for(let i=0;i<cases.length;i++){const c=cases[i],o=outputs[i];assert.equal(o.ok,true,JSON.stringify({c,output:o}));const w=decodeSpatialWorld(o.world,c.request);
  if(c.scenario==='base')assert.ok(w.waterMask.some(v=>v));if(c.scenario==='none')assert.ok(w.waterMask.every(v=>v===0));
  if(c.scenario==='lake')assert.deepEqual(w.waterMask,w.featureMasks.feature);
  if(c.scenario==='river')for(let y=0;y<w.height;y++)assert.ok(w.waterMask.subarray(y*w.width,(y+1)*w.width).some(v=>v));
  assert.equal(w.worldType,c.worldType);for(const d of w.placementDiagnostics)assert.equal(d.placedCount+Object.values(d.rejected).reduce((n,v)=>n+v,0),d.candidateCount);
 }
});
