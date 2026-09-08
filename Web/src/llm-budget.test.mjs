import test from 'node:test';import assert from 'node:assert/strict';
import {mkdtempSync,rmSync} from 'node:fs';import {tmpdir} from 'node:os';import {join} from 'node:path';import {LlmBudget} from './llm-budget.mjs';
test('paid evaluator reserves before calls, persists unknown spend and rejects unpriced/overbudget calls',t=>{
 const dir=mkdtempSync(join(tmpdir(),'llm2pcg-budget-'));t.after(()=>rmSync(dir,{recursive:true,force:true}));const path=join(dir,'ledger.json');
 const b=new LlmBudget(path,{limitUsd:.05}),body=JSON.stringify({model:'gpt-5.4-mini',max_output_tokens:3500});
 const e=b.reserve(body);assert.ok(b.reservedUsd>0);assert.equal(new LlmBudget(path,{limitUsd:.05}).reservedUsd,b.reservedUsd);
 assert.throws(()=>b.reserve(body),/EXHAUSTED/);assert.throws(()=>b.reserve(JSON.stringify({model:'unknown'})),/Unpriced/);
 b.settle(e,{input_tokens:1000,output_tokens:500,input_tokens_details:{cached_tokens:100}});assert.equal(b.reservedUsd,.0029325);
 assert.throws(()=>b.settle(e,{}),/Unknown/);
});
