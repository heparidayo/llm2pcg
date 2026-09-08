import test from 'node:test';import assert from 'node:assert/strict';
import {defaultRequest} from '../../Shared/pcg-request.mjs';import {validateRequestV4} from '../../Shared/pcg-request-v4.mjs';
import {proposeV4Migration} from './migrate-v3-to-v4.mjs';
test('explicit v3 migration preserves numeric identity and zero-cap semantics without changing the source',()=>{
 for(const type of ['Forest','Desert','Snowfield','Swamp']){
  const source=defaultRequest(type,-19,64,128),before=structuredClone(source),proposal=proposeV4Migration(source);
  assert.equal(proposal.requiresReview,true);assert.equal(validateRequestV4(proposal.request),null);assert.equal(proposal.request.seed,-19);
  assert.equal(proposal.request.mapWidth,64);assert.equal(proposal.request.mapHeight,128);assert.deepEqual(source,before);
  for(const rule of proposal.request.distributionRules){assert.notEqual(rule.amountMode,'Off');assert.equal(rule.maxCount,10000);}
  assert.ok(proposal.warnings.some(w=>w.includes('NOT an identical')));
 }
});
test('migration preserves Off, allowed types and positive caps; refuses unmapped types and worlds',()=>{
 const source=defaultRequest('Forest');source.visualSettings.trees={density:.7,maxCount:18,allowedTypes:['cherry_blossom']};source.visualSettings.rocks.density=0;
 const p=proposeV4Migration(source).request;assert.deepEqual(p.distributionRules.find(r=>r.category==='trees').types,['cherry_blossom']);assert.equal(p.distributionRules.find(r=>r.category==='trees').maxCount,18);assert.equal(p.distributionRules.find(r=>r.category==='rocks').amountMode,'Off');
 source.presentationSettings.propsEnabled=false;assert.ok(proposeV4Migration(source).request.distributionRules.every(r=>r.amountMode==='Off'));
 source.visualSettings.trees.allowedTypes=['cactus'];assert.throws(()=>proposeV4Migration(source),/Unsupported v4 type/);
 assert.throws(()=>proposeV4Migration(defaultRequest('Dungeon')),/adapter/);
 assert.throws(()=>proposeV4Migration({...source,schemaVersion:2}),/explicit v3/);
});
