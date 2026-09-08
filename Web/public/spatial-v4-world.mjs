// Data-only v2 decoder and presentation geometry. Never generates or relocates props.
export const semanticTypes={trees:['broadleaf','cherry_blossom','conifer','willow','dead_tree'],rocks:['rock'],bushes:['bush'],groundDetails:['grass','flower','mushroom'],waterProps:['reeds','lily_pad','water_lily']};
const integer=(n,lo,hi)=>Number.isInteger(n)&&n>=lo&&n<=hi;
function need(ok,message){if(!ok)throw Error('INVALID_WORLD_V2: '+message);}
function mask(raw,n,label){
  need(typeof raw==='string'&&raw.length===4*Math.ceil(n/3)&&/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(raw),label+' encoding');
  const binary=atob(raw);need(binary.length===n,label+' length');
  const data=Uint8Array.from(binary,c=>c.charCodeAt(0));need(data.every(v=>v===0||v===1),label+' bits');return data;
}
export function decodeSpatialWorld(raw,request){
  need(raw?.formatVersion===2,'formatVersion must be 2 (legacy v1 has a separate decoder)');
  need(({Forest:'forest-biome@2',Desert:'desert-biome@2',Snowfield:'snowfield-biome@2',Swamp:'swamp-biome@2'})[raw.worldType]===raw.generatorVersion,'unsupported world/generator');
  need(integer(raw.width,16,500)&&integer(raw.height,16,500)&&integer(raw.seed,-2147483648,2147483647),'dimensions/seed');
  need(request?.schemaVersion===4&&request.worldType===raw.worldType&&request.generatorVersion===raw.generatorVersion&&request.mapWidth===raw.width&&request.mapHeight===raw.height&&request.seed===raw.seed,'request/world mismatch');
  need(integer(request.terrain?.waterLevelUnits,-2147483648,2147483647),'water level');
  const n=raw.width*raw.height;
  need(Array.isArray(raw.elevationUnits)&&raw.elevationUnits.length===n&&raw.elevationUnits.every(v=>integer(v,-2147483648,2147483647)),'elevation');
  need(/^[a-f0-9]{64}$/.test(raw.worldHash)&&/^[a-f0-9]{64}$/.test(raw.semanticHash),'hash identifiers');
  need(integer(raw.startIndex,0,n-1)&&integer(raw.exitIndex,0,n-1),'anchors');
  const world={...raw,elevationUnits:Int32Array.from(raw.elevationUnits)};
  for(const key of ['waterMask','routeMask','bridgeMask','protectedMask'])world[key]=mask(raw[key],n,key);
  need(world.bridgeMask.every((v,i)=>!v||(world.waterMask[i]&&world.routeMask[i])),'bridge must preserve water and route');
  need(raw.featureMasks&&typeof raw.featureMasks==='object'&&!Array.isArray(raw.featureMasks),'featureMasks');
  const ids=request.spatialFeatures.map(f=>f.id);need(Object.keys(raw.featureMasks).length===ids.length,'feature count');
  world.featureMasks=Object.fromEntries(ids.map(id=>[id,mask(raw.featureMasks[id],n,'feature '+id)]));
  const cap=request.distributionRules.reduce((sum,r)=>sum+r.maxCount,0);
  need(Array.isArray(raw.placements)&&raw.placements.length<=cap,'placements/count cap');
  const seen=new Set();
  world.placements=raw.placements.map(p=>{
    need(p&&semanticTypes[p.category]?.includes(p.type),'semantic type');
    need(integer(p.x,0,raw.width-1)&&integer(p.y,0,raw.height-1)&&integer(p.elevationUnits,-2147483648,2147483647)&&integer(p.yawDegrees,0,359)&&integer(p.scalePermille,850,1150),'placement transform');
    need(p.stableId===`${p.category}:${p.x}:${p.y}`&&!seen.has(p.stableId),'placement identity');seen.add(p.stableId);
    const rule=request.distributionRules.find(r=>r.category===p.category);need(rule&&rule.types.includes(p.type)&&rule.amountMode!=='Off','placement violates request type/off');
    need(p.elevationUnits===world.elevationUnits[p.y*raw.width+p.x],'placement elevation');return {...p};
  });
  for(const rule of request.distributionRules)need(world.placements.filter(p=>p.category===rule.category).length<=rule.maxCount,'category cap');
  return world;
}
export const WATER_OFFSET=.08;
export function groundHeight(w,i){return w.elevationUnits[i]*.25-(w.waterMask[i] ? .25 : 0);}
function neighbours(i,w,h){const x=i%w,y=Math.floor(i/w);return [x>0?i-1:-1,x+1<w?i+1:-1,y>0?i-w:-1,y+1<h?i+w:-1].filter(i=>i>=0);}
export function bridgeHeights(world,waterY){
  const heights=new Float32Array(world.width*world.height),visited=new Uint8Array(heights.length);
  for(let start=0;start<heights.length;start++){
    if(!world.bridgeMask[start]||visited[start])continue;
    const queue=[start];visited[start]=1;let deck=waterY+.25;
    for(let at=0;at<queue.length;at++)for(const next of neighbours(queue[at],world.width,world.height)){
      if(!world.waterMask[next]&&world.routeMask[next])deck=Math.max(deck,groundHeight(world,next)+.12);
      if(world.bridgeMask[next]&&!visited[next]){visited[next]=1;queue.push(next);}
    }
    for(const i of queue)heights[i]=deck;
  }
  return heights;
}
export function surfaceGeometry(world,request){
  const gw=world.width*2+1,gh=world.height*2+1,positions=[],triangles=Array.from({length:5},()=>[]);
  for(let gy=0;gy<gh;gy++)for(let gx=0;gx<gw;gx++){
    let sum=0,count=0;
    for(let y=Math.floor((gy-1)/2);y<=Math.floor(gy/2);y++)for(let x=Math.floor((gx-1)/2);x<=Math.floor(gx/2);x++)
      if(x>=0&&y>=0&&x<world.width&&y<world.height){sum+=groundHeight(world,y*world.width+x);count++;}
    positions.push(gx*.5-.5,sum/count,gy*.5-.5);
  }
  const waterY=request.terrain.waterLevelUnits*.25+WATER_OFFSET,decks=bridgeHeights(world,waterY);
  function quad(slot,x,y,height){const b=positions.length/3;positions.push(x-.5,height,y-.5,x+.5,height,y-.5,x+.5,height,y+.5,x-.5,height,y+.5);triangles[slot].push(b,b+2,b+1,b,b+3,b+2);}
  for(let y=0;y<world.height;y++)for(let x=0;x<world.width;x++){
    const i=y*world.width+x,bl=y*2*gw+x*2,center=bl+gw+1,ring=[bl,bl+1,bl+2,bl+gw+2,bl+gw*2+2,bl+gw*2+1,bl+gw*2,bl+gw];
    const slot=world.waterMask[i]?2:world.routeMask[i]?1:0;
    for(let k=0;k<8;k++)triangles[slot].push(center,ring[(k+1)%8],ring[k]);
    if(world.waterMask[i])quad(3,x,y,waterY);if(world.bridgeMask[i])quad(4,x,y,decks[i]);
  }
  return {positions:Float32Array.from(positions),triangles:triangles.map(t=>Uint32Array.from(t))};
}
// Explicit glyphs mirror Unity primitive-forest@1 shapes. Colors/pixels depend on the engine.
export function glyphBoxes(category,type){
  need(semanticTypes[category]?.includes(type),'glyph type');const boxes=[];
  const color={cherry_blossom:0xf278a8,conifer:0x19523a,willow:0x709e2e,dead_tree:0x57402b,rock:0x6e787d,flower:0xfabe3d,mushroom:0xc74a38,water_lily:0xfab8e0,reeds:0x949638}[type]??0x408233;
  const box=(position,size,c=color)=>boxes.push({position,size,color:c});
  if(category==='trees'){
    box([0,1.1,0],[.24,2.2,.24],0x4f3621);
    if(type==='dead_tree'){box([.3,1.6,0],[.8,.16,.15],0x4f3621);box([0,1.95,-.25],[.15,.15,.7],0x4f3621);}
    else if(type==='conifer'){for(let k=0;k<3;k++)box([0,1.45+k*.6,0],[1.3-k*.35,.65,1.3-k*.35]);}
    else {box([0,2.15,0],[1.45,.95,1.45]);box([0,2.75,0],[.9,.4,.9]);if(type==='willow')for(let k=0;k<4;k++)box([k<2?(k===0?-.62:.62):0,1.25,k>=2?(k===2?-.62:.62):0],[.22,1.6,.22]);}
  }else if(type==='rock')box([0,.35,0],[.9,.7,.7]);
  else if(type==='bush')box([0,.3,0],[.8,.6,.7]);
  else if(['lily_pad','water_lily'].includes(type)){box([0,.025,0],[.6,.05,.6],0x387a33);if(type==='water_lily')box([0,.12,0],[.22,.15,.22]);}
  else{const tall=type==='reeds'?.85:.3;box([0,tall*.5,0],[.09,tall,.09],0x406b29);box([0,tall,0],[type==='mushroom'?.35:.2,.12,.2]);}
  return boxes;
}
