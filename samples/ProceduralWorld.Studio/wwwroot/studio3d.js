import * as THREE from 'three';
import { OrbitControls } from './vendor/OrbitControls.js';

const canvas = document.getElementById('terrain3d');
let renderer, scene, camera, controls, worldRoot, activeWorld, activeView = 'terrain';

function initialize() {
  if (renderer) return true;
  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false, powerPreference: 'high-performance' });
  } catch (error) {
    console.warn('WebGL indisponível; usando a miniatura Canvas.', error);
    return false;
  }
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  renderer.setSize(canvas.width, canvas.height, false);
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.12;

  scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0b100f);
  scene.fog = new THREE.FogExp2(0x0b100f, .008);
  camera = new THREE.PerspectiveCamera(42, canvas.width / canvas.height, .1, 500);
  camera.position.set(88, 68, 92);
  controls = new OrbitControls(camera, canvas);
  controls.target.set(0, 5, 0);
  controls.enableDamping = true;
  controls.dampingFactor = .07;
  controls.minDistance = 45;
  controls.maxDistance = 210;
  controls.maxPolarAngle = Math.PI * .48;

  scene.add(new THREE.HemisphereLight(0xbad5e4, 0x252118, 1.35));
  const sun = new THREE.DirectionalLight(0xffe0aa, 3.2);
  sun.position.set(-55, 90, 35);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  sun.shadow.camera.left = sun.shadow.camera.bottom = -75;
  sun.shadow.camera.right = sun.shadow.camera.top = 75;
  sun.shadow.camera.near = 5;
  sun.shadow.camera.far = 210;
  sun.shadow.bias = -.0004;
  scene.add(sun);

  const floor = new THREE.Mesh(new THREE.PlaneGeometry(380, 380), new THREE.MeshStandardMaterial({ color: 0x111614, roughness: .95 }));
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = -17;
  floor.receiveShadow = true;
  scene.add(floor);
  const grid = new THREE.GridHelper(380, 38, 0x303b37, 0x1b2421);
  grid.position.y = -16.9;
  scene.add(grid);
  animate();
  return true;
}

function disposeObject(root) {
  root?.traverse(object => {
    object.geometry?.dispose();
    if (Array.isArray(object.material)) object.material.forEach(material => material.dispose());
    else object.material?.dispose();
  });
  if (root) scene.remove(root);
}

function biomeColor(biome) {
  return [0x326d7b,0x315a78,0xd7c58e,0xb99769,0xa4a765,0x6d9a64,0x3f7657,0x2e6550,0x536d57,0x648075,0xa6aaa0,0x77746d,0xe2e6e4][biome] ?? 0x758078;
}

function terrainColor(world, index, normalized, view) {
  if (view === 'biomes') return new THREE.Color(biomeColor(world.biomes[index]));
  if (view === 'slope') return new THREE.Color().lerpColors(new THREE.Color(0xe1e2da), new THREE.Color(0x393f3d), Math.min(1, world.slope[index] / 1.3));
  if (view === 'water') return new THREE.Color(world.waterDepth[index] > .01 ? 0x238fc2 : 0x263d32);
  if (world.waterDepth[index] > .04) return new THREE.Color(0x347fa5);
  if (normalized > .82) return new THREE.Color(0xe4e8e4);
  if (normalized > .62 && world.slope[index] > .38) return new THREE.Color(0x77756d);
  if (normalized > .55) return new THREE.Color(0x70815d);
  if (normalized > .25) return new THREE.Color(0x47704d);
  return new THREE.Color(0x5c7d50);
}

function createTerrain(world, view, root) {
  const n = world.size, count = n * n, min = world.report.minimumHeight, max = world.report.maximumHeight, span = Math.max(1, max - min);
  const positions = new Float32Array(count * 3), colors = new Float32Array(count * 3), indices = [];
  for (let z = 0; z < n; z++) for (let x = 0; x < n; x++) {
    const i = z * n + x, p = i * 3, normalized = (world.height[i] - min) / span;
    positions[p] = x / (n - 1) * 100 - 50;
    positions[p + 1] = normalized * 34;
    positions[p + 2] = z / (n - 1) * 100 - 50;
    terrainColor(world, i, normalized, view).toArray(colors, p);
  }
  for (let z = 0; z < n - 1; z++) for (let x = 0; x < n - 1; x++) {
    const a = z * n + x, b = a + 1, c = a + n, d = c + 1;
    indices.push(a, c, d, a, d, b);
  }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  geometry.setIndex(indices);
  geometry.computeVertexNormals();
  const terrain = new THREE.Mesh(geometry, new THREE.MeshStandardMaterial({ vertexColors: true, roughness: .88, metalness: 0, flatShading: false }));
  terrain.castShadow = terrain.receiveShadow = true;
  root.add(terrain);
  createWalls(world, min, span, root);
  createWater(world, min, span, root);
  createDetails(world, min, span, root, view);
}

function point(world, x, z, min, span, yOffset = 0) {
  const i = z * world.size + x;
  return new THREE.Vector3(x / (world.size - 1) * 100 - 50, (world.height[i] - min) / span * 34 + yOffset, z / (world.size - 1) * 100 - 50);
}

function createWalls(world, min, span, root) {
  const n = world.size, boundaries = [
    Array.from({length:n},(_,x)=>[x,0]), Array.from({length:n},(_,z)=>[n-1,z]),
    Array.from({length:n},(_,x)=>[n-1-x,n-1]), Array.from({length:n},(_,z)=>[0,n-1-z])
  ];
  for (const boundary of boundaries) {
    const positions = [], indices = [];
    boundary.forEach(([x,z]) => { const p = point(world,x,z,min,span); positions.push(p.x,p.y,p.z,p.x,-14,p.z); });
    for (let i=0;i<boundary.length-1;i++){const a=i*2,b=a+1,c=a+2,d=a+3;indices.push(a,b,d,a,d,c)}
    const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setIndex(indices);geometry.computeVertexNormals();
    const wall=new THREE.Mesh(geometry,new THREE.MeshStandardMaterial({color:0x4b4a42,roughness:1}));wall.castShadow=wall.receiveShadow=true;root.add(wall);
  }
}

function createWater(world, min, span, root) {
  const n=world.size, positions=[],indices=[];
  for(let z=0;z<n-1;z++)for(let x=0;x<n-1;x++){const ids=[z*n+x,z*n+x+1,(z+1)*n+x+1,(z+1)*n+x];if(!ids.some(i=>world.waterDepth[i]>.02))continue;const base=positions.length/3;for(const i of ids){const px=i%n,pz=Math.floor(i/n),p=point(world,px,pz,min,span,.18+Math.min(1.2,world.waterDepth[i]*.08));positions.push(p.x,p.y,p.z)}indices.push(base,base+1,base+2,base,base+2,base+3)}
  if(!indices.length)return;const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(positions,3));geometry.setIndex(indices);geometry.computeVertexNormals();
  const water=new THREE.Mesh(geometry,new THREE.MeshPhysicalMaterial({color:0x187fb4,roughness:.16,metalness:.08,transparent:true,opacity:.78,clearcoat:1,clearcoatRoughness:.12}));water.receiveShadow=true;root.add(water);
}

function createDetails(world,min,span,root,view){
  if(view!=='terrain'&&view!=='biomes')return;const n=world.size,dummy=new THREE.Object3D(),treePoints=[],rockPoints=[],cavePoints=[];
  const stride=Math.max(2,Math.floor(n/52));for(let z=2;z<n-2;z+=stride)for(let x=2;x<n-2;x+=stride){const i=z*n+x,hash=((x*73856093)^(z*19349663)^world.seed)>>>0;if((world.biomes[i]===6||world.biomes[i]===7||world.biomes[i]===9)&&world.waterDepth[i]<.01&&hash%5<2)treePoints.push([x,z,hash]);else if(world.slope[i]>.58&&hash%19===0)rockPoints.push([x,z,hash])}
  for(let z=0;z<n;z++)for(let x=0;x<n;x++){const i=z*n+x;if(world.caveOpening?.[i]&&((x+z)%3===0))cavePoints.push([x,z,0])}
  const trees=new THREE.InstancedMesh(new THREE.ConeGeometry(.48,2.7,7),new THREE.MeshStandardMaterial({color:0x214c2d,roughness:.94}),treePoints.length);trees.castShadow=true;treePoints.forEach(([x,z,hash],i)=>{const p=point(world,x,z,min,span,1.3);dummy.position.copy(p);const s=.72+(hash%100)/250;dummy.scale.set(s,s,s);dummy.rotation.y=(hash%628)/100;dummy.updateMatrix();trees.setMatrixAt(i,dummy.matrix)});root.add(trees);
  const rocks=new THREE.InstancedMesh(new THREE.DodecahedronGeometry(.65,0),new THREE.MeshStandardMaterial({color:0x68665d,roughness:1}),rockPoints.length);rocks.castShadow=true;rockPoints.forEach(([x,z,hash],i)=>{dummy.position.copy(point(world,x,z,min,span,.35));const s=.5+(hash%80)/100;dummy.scale.set(s,s*.7,s);dummy.rotation.set(hash%7,hash%11,hash%5);dummy.updateMatrix();rocks.setMatrixAt(i,dummy.matrix)});root.add(rocks);
  const caves=new THREE.InstancedMesh(new THREE.SphereGeometry(.7,10,7),new THREE.MeshStandardMaterial({color:0x050605,roughness:1}),cavePoints.length);cavePoints.forEach(([x,z],i)=>{dummy.position.copy(point(world,x,z,min,span,.2));dummy.scale.set(1,.7,.45);dummy.updateMatrix();caves.setMatrixAt(i,dummy.matrix)});root.add(caves);
}

function render(world, view) {
  if (!initialize()) return false;
  activeWorld = world; activeView = view;
  disposeObject(worldRoot);
  worldRoot = new THREE.Group();
  scene.add(worldRoot);
  createTerrain(world, view, worldRoot);
  return true;
}

function animate() {
  requestAnimationFrame(animate);
  controls?.update();
  if (renderer && scene && camera && !canvas.classList.contains('hidden')) renderer.render(scene, camera);
}

window.Studio3D = { render, available: () => !!renderer || initialize() };
window.dispatchEvent(new Event('studio3d-ready'));
