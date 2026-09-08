// Conservative reservation is durable BEFORE transmission. An interrupted/unknown call
// keeps its full reservation; it is never assumed to have cost zero on restart.
import {readFileSync,writeFileSync,openSync,closeSync,fsyncSync} from 'node:fs';
export class LlmBudget {
  constructor(path,{limitUsd=1}={}){
    if(!(limitUsd>0&&limitUsd<=1))throw Error('Budget must be within the approved $1 ceiling.');
    this.path=path;this.limit=limitUsd;
    try{this.state=JSON.parse(readFileSync(path,'utf8'));}catch(e){if(e.code!=='ENOENT')throw e;this.state={limitUsd,entries:[]};}
    if(this.state.limitUsd!==limitUsd)throw Error('Budget ledger limit mismatch.');
  }
  save(){writeFileSync(this.path,JSON.stringify(this.state,null,2));const fd=openSync(this.path,'r+');try{fsyncSync(fd);}finally{closeSync(fd);}}
  get reservedUsd(){return this.state.entries.reduce((n,e)=>n+(e.actualUsd??e.reservedUsd),0);}
  reserve(body){
    if(this.state.entries.some(e=>e.actualUsd>e.reservedUsd))throw Error('Budget estimate violation: ledger locked.');
    const b=JSON.parse(body);
    if(!['gpt-5.4-mini','gpt-5.4-mini-2026-03-17'].includes(b.model)||b.tools?.length||b.previous_response_id||!Number.isInteger(b.max_output_tokens)||b.max_output_tokens>3500)throw Error('Unpriced request rejected.');
    // UTF-8 byte count is deliberately pessimistic for text token count; add ample
    // schema/message overhead. Tools/history/images are not used by this evaluator.
    const reservedUsd=(Buffer.byteLength(body)+16384)*.75/1e6+b.max_output_tokens*4.5/1e6;
    if(this.reservedUsd+reservedUsd>this.limit)throw Error('APPROVED_LLM_BUDGET_EXHAUSTED');
    const entry={id:this.state.entries.length,reservedUsd,state:'reserved',utc:new Date().toISOString()};this.state.entries.push(entry);this.save();return entry;
  }
  settle(entry,usage){
    const input=usage?.input_tokens,output=usage?.output_tokens,cached=usage?.input_tokens_details?.cached_tokens??0;
    if(![input,output,cached].every(Number.isSafeInteger)||input<0||output<0||cached<0||cached>input)throw Error('Unknown usage: reservation retained.');
    const actualUsd=((input-cached)*.75+cached*.075+output*4.5)/1e6;
    entry.usage=usage;entry.actualUsd=actualUsd;entry.state='settled';this.save();
    if(actualUsd>entry.reservedUsd||this.reservedUsd>this.limit)throw Error('Reservation assumption exceeded: no further calls allowed.');
  }
}
