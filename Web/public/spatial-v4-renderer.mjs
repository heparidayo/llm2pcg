import * as THREE from 'three';
import {OrbitControls} from '/vendor/OrbitControls.js';
import {decodeSpatialWorld,surfaceGeometry,glyphBoxes,semanticTypes,WATER_OFFSET} from './spatial-v4-world.mjs';

export class SpatialV4Renderer {
  constructor(container){
    this.container=container;this.scene=new THREE.Scene();this.scene.background=new THREE.Color(0x192630);
    this.camera=new THREE.OrthographicCamera(-1,1,1,-1,.1,3000);
    this.gpu=new THREE.WebGLRenderer({antialias:true,preserveDrawingBuffer:true});this.gpu.setPixelRatio(Math.min(devicePixelRatio||1,2));
    this.gpu.domElement.setAttribute('aria-label','v4 Core 전체 월드의 3D 프리뷰');
    // Drawing-buffer pixels include devicePixelRatio; CSS dimensions must not.
    this.gpu.domElement.style.width='100%';this.gpu.domElement.style.height='100%';container.replaceChildren(this.gpu.domElement);
    this.scene.add(new THREE.HemisphereLight(0xd6e7ec,0x455536,2));const sun=new THREE.DirectionalLight(0xfff4dc,2);sun.position.set(-60,100,-40);this.scene.add(sun);
    this.controls=new OrbitControls(this.camera,this.gpu.domElement);this.controls.enableDamping=true;this.controls.maxPolarAngle=Math.PI*.49;
    this.resizeObserver=new ResizeObserver(()=>this.resize());this.resizeObserver.observe(container);
    this.animate=()=>{this.controls.update();this.gpu.render(this.scene,this.camera);this.frame=requestAnimationFrame(this.animate);};this.resize();this.animate();
  }
  resize(){const width=Math.max(1,this.container.clientWidth),height=Math.max(1,this.container.clientHeight),span=this.span??40;this.gpu.setSize(width,height,false);this.camera.left=-span*width/height;this.camera.right=span*width/height;this.camera.top=span;this.camera.bottom=-span;this.camera.updateProjectionMatrix();}
  static release(root){if(!root)return;const materials=new Set(),geometries=new Set();root.traverse(obj=>{if(obj.geometry)geometries.add(obj.geometry);if(obj.material)for(const m of Array.isArray(obj.material)?obj.material:[obj.material])materials.add(m);if(obj.isInstancedMesh)obj.dispose();});for(const g of geometries)g.dispose();for(const m of materials)m.dispose();}
  render(raw,request){
    const world=decodeSpatialWorld(raw,request),root=new THREE.Group();
    try{
      const surface=surfaceGeometry(world,request),geometry=new THREE.BufferGeometry();
      geometry.setAttribute('position',new THREE.BufferAttribute(surface.positions,3));
      const index=new Uint32Array(surface.triangles.reduce((n,t)=>n+t.length,0));let offset=0;
      surface.triangles.forEach((t,slot)=>{index.set(t,offset);geometry.addGroup(offset,t.length,slot);offset+=t.length;});
      geometry.setIndex(new THREE.BufferAttribute(index,1));geometry.computeVertexNormals();geometry.computeBoundingSphere();
      const palette=world.worldType==='Desert'?[0xcbaa68,0xa88b59,0x82704b,0x279fba,0x8c5229]:world.worldType==='Snowfield'?[0xe4edf2,0x939fae,0x5d7587,0x8bc3df,0x8c5229]:world.worldType==='Swamp'?[0x4a5736,0x77704e,0x3c4531,0x425f47,0x8c5229]:[0x4a6e36,0x9c784a,0x4c574a,0x1f7ba3,0x8c5229];
      root.add(new THREE.Mesh(geometry,palette.map(color=>new THREE.MeshStandardMaterial({color,roughness:1}))));
      const categories=Object.keys(semanticTypes);
      for(const category of categories)for(const type of semanticTypes[category]){
        const placements=world.placements.filter(p=>p.category===category&&p.type===type);if(!placements.length)continue;
        for(const box of glyphBoxes(category,type)){
          const mesh=new THREE.InstancedMesh(new THREE.BoxGeometry(...box.size),new THREE.MeshStandardMaterial({color:box.color,roughness:1}),placements.length);
          mesh.name=category+'/'+type;root.add(mesh);const matrix=new THREE.Matrix4(),local=new THREE.Matrix4().makeTranslation(...box.position),rotation=new THREE.Quaternion();
          placements.forEach((p,i)=>{rotation.setFromAxisAngle(new THREE.Vector3(0,1,0),p.yawDegrees*Math.PI/180);const height=category==='waterProps'?request.terrain.waterLevelUnits*.25+WATER_OFFSET:p.elevationUnits*.25;matrix.compose(new THREE.Vector3(p.x,height,p.y),rotation,new THREE.Vector3().setScalar(p.scalePermille*.001));matrix.multiply(local);mesh.setMatrixAt(i,matrix);});
          mesh.instanceMatrix.needsUpdate=true;mesh.computeBoundingSphere();
        }
      }
    }catch(error){SpatialV4Renderer.release(root);throw error;}
    this.scene.add(root);if(this.root)this.scene.remove(this.root);SpatialV4Renderer.release(this.root);this.root=root;this.world=world;
    this.fitCamera(false);
    return {completed:true,profile:world.worldType==='Forest'?'web-primitive-forest@1':'web-primitive-'+world.worldType.toLowerCase()+'@1',worldHash:world.worldHash,semanticHash:world.semanticHash,
      counts:Object.keys(semanticTypes).map(category=>({category,placedCount:world.placements.filter(p=>p.category===category).length,renderedCount:world.placements.filter(p=>p.category===category).length}))};
  }
  fitCamera(topDown){if(!this.world)return;const w=this.world,span=Math.max(w.width,w.height),height=w.elevationUnits.reduce((a,b)=>Math.max(a,b),0)*.25;this.span=span*.62+height*.2;
    const center=new THREE.Vector3((w.width-1)*.5,height*.3,(w.height-1)*.5);this.camera.position.copy(center).add(topDown?new THREE.Vector3(0,span*2,.001):new THREE.Vector3(-span*.75,span*1.3,-span*.9));this.controls.target.copy(center);this.camera.lookAt(center);this.controls.update();this.resize();}
  capture(){this.gpu.render(this.scene,this.camera);return this.gpu.domElement.toDataURL('image/png');}
  dispose(){cancelAnimationFrame(this.frame);this.resizeObserver.disconnect();this.controls.dispose();SpatialV4Renderer.release(this.root);this.gpu.dispose();this.container.replaceChildren();}
}
