import * as THREE from "three";
import { OrbitControls } from "/vendor/OrbitControls.js";
import { GLTFLoader } from "/vendor/addons/loaders/GLTFLoader.js";
import { decodeGeneratedWorld } from "/world-codec.mjs";

const BIOME_PALETTES = {
  Forest: { ground: 0x466947, water: 0x38789b, path: 0x9a8058, road: 0x716b62, park: 0x587b4a, building: 0x83909a },
  Swamp: { ground: 0x455844, water: 0x314f55, path: 0x766b4f, road: 0x655f52, park: 0x52664b, building: 0x7b8177 },
  Snowfield: { ground: 0xcad9df, water: 0x72a9c2, path: 0xaebcc1, road: 0x88969d, park: 0xb9d0d4, building: 0x8798a4 },
  Desert: { ground: 0xc49a62, water: 0x3c91a5, path: 0xa67849, road: 0x806954, park: 0x87945b, building: 0xb8895e },
  City: { ground: 0x59636c, water: 0x3d748c, path: 0x777167, road: 0x2d343b, park: 0x527153, building: 0x8b98a3 }
};

export class WorldRenderer {
  constructor(container, assetManifest = null) {
    this.container = container;
    this.assetManifest = assetManifest;
    this.gltfLoader = new GLTFLoader();
    this.modelPoolPromises = new Map();
    this.modelPools = new Map();
    this.renderRevision = 0;
    this.usedModelAssets = false;
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x101820);
    this.scene.fog = new THREE.FogExp2(0x101820, 0.0065);
    this.camera = new THREE.PerspectiveCamera(46, 1, 0.1, 2400);
    this.camera.position.set(52, 58, 52);
    this.renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: "high-performance" });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.6));
    this.renderer.setSize(1, 1, false);
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.domElement.setAttribute("aria-label", "생성된 절차적 월드 3D 뷰");
    this.renderer.domElement.tabIndex = 0;
    container.replaceChildren(this.renderer.domElement);

    this.controls = new OrbitControls(this.camera, this.renderer.domElement);
    this.controls.enableDamping = true;
    this.controls.dampingFactor = 0.075;
    this.controls.screenSpacePanning = false;
    this.controls.minDistance = 3;
    this.controls.maxDistance = 1200;
    this.controls.maxPolarAngle = Math.PI * 0.49;

    this.worldRoot = new THREE.Group();
    this.worldRoot.name = "GeneratedWorld";
    this.scene.add(this.worldRoot);
    this.addLighting();

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(container);
    this.resize();
    this.animate = this.animate.bind(this);
    this.animationFrame = requestAnimationFrame(this.animate);
  }

  addLighting() {
    this.scene.add(new THREE.HemisphereLight(0xc8e2ef, 0x263321, 1.35));
    const key = new THREE.DirectionalLight(0xffefd6, 2.1);
    key.position.set(70, 110, 48);
    key.castShadow = true;
    key.shadow.mapSize.set(2048, 2048);
    key.shadow.camera.left = -100;
    key.shadow.camera.right = 100;
    key.shadow.camera.top = 100;
    key.shadow.camera.bottom = -100;
    key.shadow.camera.far = 360;
    this.scene.add(key);
    const fill = new THREE.DirectionalLight(0x78a8c7, 0.55);
    fill.position.set(-40, 36, -55);
    this.scene.add(fill);
  }

  async render(document, request = null) {
    const revision = ++this.renderRevision;
    const decodedWorld = decodeGeneratedWorld(document);
    const presentation = request?.presentationSettings ?? { geometryMode: "Surface", propsEnabled: true };
    await this.preloadWorldAssets(decodedWorld, presentation);
    if (revision !== this.renderRevision) return null;
    this.clearWorld();
    this.usedModelAssets = false;
    this.world = decodedWorld;
    this.request = request;
    this.presentation = presentation;
    if (this.world.worldType === "Dungeon") this.renderDungeon(this.world, presentation);
    else if (this.world.worldType === "Cave") this.renderCave(this.world, presentation);
    else this.renderBiome(this.world, request, presentation);
    this.addMarkers(this.world);
    this.addWorldFrame(this.world);
    this.fitCamera("isometric");
    return this.world;
  }

  async preloadWorldAssets(world, presentation) {
    const keys = [];
    // The pure voxel stage deliberately represents every prop with boxes. It must not
    // fetch a GLB simply because the shared presentation request has props enabled.
    if (!presentation.propsEnabled || presentation.geometryMode === "Voxel") return;
    if (world.worldType === "Dungeon") {
      for (const kind of new Set((world.props || []).map(prop => prop.kind))) keys.push(`Dungeon/${kind}`);
    } else if (world.worldType === "City") {
      keys.push("City/Building/LowRise", "City/Building/MidRise", "City/Building/HighRise", "City/CityPropAnchor");
    } else if (world.worldType !== "Cave") {
      keys.push(`${world.worldType}/Vegetation`);
    }
    await Promise.all(keys.map(key => this.loadModelPool(key)));
  }

  async loadModelPool(key) {
    if (this.modelPoolPromises.has(key)) return this.modelPoolPromises.get(key);
    const entries = Array.isArray(this.assetManifest?.models?.[key]) ? this.assetManifest.models[key] : [];
    const promise = Promise.all(entries.map(async entry => {
      try {
        const gltf = await this.gltfLoader.loadAsync(entry.url);
        return this.createModelTemplate(gltf.scene, entry);
      } catch (error) {
        console.warn(`Could not load web model ${entry.id || entry.url}:`, error);
        return null;
      }
    })).then(values => {
      const pool = values.filter(Boolean);
      this.modelPools.set(key, pool);
      return pool;
    });
    this.modelPoolPromises.set(key, promise);
    return promise;
  }

  createModelTemplate(scene, entry) {
    scene.updateMatrixWorld(true);
    const bounds = new THREE.Box3().setFromObject(scene);
    if (bounds.isEmpty()) throw new Error("GLB scene has no renderable bounds.");
    const size = bounds.getSize(new THREE.Vector3());
    const center = bounds.getCenter(new THREE.Vector3());
    const normalization = new THREE.Matrix4().makeScale(
      1 / Math.max(size.x, 1e-6),
      1 / Math.max(size.y, 1e-6),
      1 / Math.max(size.z, 1e-6)
    ).multiply(new THREE.Matrix4().makeTranslation(-center.x, -bounds.min.y, -center.z));
    const primitives = [];
    scene.traverse(object => {
      if (!object.isMesh || !object.geometry || !object.material) return;
      primitives.push({
        geometry: object.geometry,
        material: object.material,
        matrix: normalization.clone().multiply(object.matrixWorld)
      });
    });
    if (!primitives.length) throw new Error("GLB scene has no mesh primitives.");
    return { entry, primitives };
  }

  renderDungeon(world, presentation) {
    const floors = [];
    const corridors = [];
    const edgeWalls = [];
    const voxelWalls = new Map();
    const voxelGeometry = presentation.geometryMode === "Voxel";
    const width = world.width;
    const height = world.height;
    const isOpen = (x, y) => x >= 0 && y >= 0 && x < width && y < height && world.cells[y * width + x] !== 0;
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const cell = world.cells[y * width + x];
        if (cell === 0) continue;
        const position = this.cellPosition(world, x, y);
        (cell === 2 ? corridors : floors).push({ x: position.x, y: voxelGeometry ? -0.1 : 0, z: position.z, width: 0.98, height: 0.2, depth: 0.98 });
        for (const [nx, ny, wx, wz, wallWidth, wallDepth] of [
          [x, y - 1, position.x, position.z - 0.48, 1.05, 0.12],
          [x + 1, y, position.x + 0.48, position.z, 0.12, 1.05],
          [x, y + 1, position.x, position.z + 0.48, 1.05, 0.12],
          [x - 1, y, position.x - 0.48, position.z, 0.12, 1.05]
        ]) {
          if (isOpen(nx, ny)) continue;
          if (voxelGeometry && nx >= 0 && ny >= 0 && nx < width && ny < height) {
            const closed = this.cellPosition(world, nx, ny);
            voxelWalls.set(`${nx}:${ny}`, { x: closed.x, y: 0.9, z: closed.z, width: 0.98, height: 1.8, depth: 0.98 });
          } else edgeWalls.push({ x: wx, y: 0.82, z: wz, width: wallWidth, height: 1.7, depth: wallDepth });
        }
      }
    }
    if (voxelGeometry) {
      this.addBoxes(floors, 0x4e5864, { roughness: 0.94 });
      this.addBoxes(corridors, 0x826e4f, { roughness: 0.9 });
      this.addBoxes([...voxelWalls.values()], 0x252d36, { roughness: 0.97 });
    } else {
      this.addFlatTiles(floors, 0x4e5864, { roughness: 0.94 });
      this.addFlatTiles(corridors, 0x826e4f, { roughness: 0.9 });
      this.addBoxes(edgeWalls, 0x252d36, { roughness: 0.97 });
    }
    if (presentation.propsEnabled) this.renderDungeonProps(world, presentation);
    this.renderRoomRoles(world);
  }

  renderDungeonProps(world, presentation) {
    const groups = new Map();
    for (const prop of world.props || []) {
      const position = this.cellPosition(world, prop.x, prop.y);
      if (!groups.has(prop.kind)) groups.set(prop.kind, []);
      groups.get(prop.kind).push({ ...position, semanticIndex: prop.semanticIndex });
    }
    if (presentation.geometryMode === "Voxel") return this.renderVoxelDungeonProps(groups);
    if (!this.renderModelPool("Dungeon/Pillar", groups.get("Pillar") || [], () => [0.55, 1.35, 0.55])) this.addCylinders(groups.get("Pillar") || [], 0.25, 1.35, 0x77736c, 10);
    if (!this.renderModelPool("Dungeon/Crate", groups.get("Crate") || [], () => [0.55, 0.55, 0.55])) this.addBoxes((groups.get("Crate") || []).map(p => ({ ...p, y: 0.28, width: 0.52, height: 0.56, depth: 0.52 })), 0x8d5830);
    if (!this.renderModelPool("Dungeon/Crystal", groups.get("Crystal") || [], () => [0.5, 0.8, 0.5])) this.addCones(groups.get("Crystal") || [], 0.25, 0.8, 0x6ed6e7, 5, 0.42, true);
    if (!this.renderModelPool("Dungeon/Torch", groups.get("Torch") || [], () => [0.25, 0.85, 0.25])) {
      this.addCylinders(groups.get("Torch") || [], 0.08, 0.7, 0x77462a, 7);
      this.addSpheres((groups.get("Torch") || []).map(p => ({ ...p, y: 0.78 })), 0.14, 0xff9b37, true);
    }
  }

  renderVoxelDungeonProps(groups) {
    const boxes = (kind, color, width, height, depth) => this.addBoxes((groups.get(kind) || []).map(point => ({
      ...point, y: height / 2, width, height, depth
    })), color, { roughness: 0.92 });
    boxes("Pillar", 0x77736c, 0.48, 1.38, 0.48);
    boxes("Crate", 0x8d5830, 0.58, 0.58, 0.58);
    boxes("Crystal", 0x59c6d8, 0.5, 0.82, 0.5);
    boxes("Torch", 0xff9b37, 0.28, 0.9, 0.28);
  }

  renderRoomRoles(world) {
    const roleColors = { Boss: 0xd94c4c, Treasure: 0xf0c84c, Shop: 0xb56ce2, Secret: 0x4ed1c4 };
    for (const room of world.rooms || []) {
      const role = room.roles?.find(value => roleColors[value]);
      if (!role) continue;
      const position = this.cellPosition(world, room.x + room.width / 2 - 0.5, room.y + room.height / 2 - 0.5);
      this.addCylinders([{ ...position, y: 0.08 }], 0.34, 0.16, roleColors[role], 16, true);
    }
  }

  renderCave(world, presentation) {
    if (presentation.geometryMode === "Surface") {
      this.addGridSurface(
        world,
        (x, y) => world.cells[y * world.width + x] === 1 ? 1.65 + this.stableUnit(world.seed, x, y, 91) * 1.1 : 0,
        (x, y) => world.cells[y * world.width + x] === 1 ? 0x202a30 : 0x3e4549
      );
      return;
    }
    const floor = [];
    const rocks = [];
    for (let y = 0; y < world.height; y++) {
      for (let x = 0; x < world.width; x++) {
        const position = this.cellPosition(world, x, y);
        if (world.cells[y * world.width + x] === 1) {
          const height = 1.7 + this.stableUnit(world.seed, x, y, 91) * 1.25;
          rocks.push({ x: position.x, y: height / 2 - 0.08, z: position.z, width: 0.98, height, depth: 0.98, yaw: this.stableUnit(world.seed, x, y, 93) * Math.PI * 0.12 });
        } else {
          floor.push({ x: position.x, y: -0.06, z: position.z, width: 0.98, height: 0.12, depth: 0.98 });
        }
      }
    }
    this.addBoxes(floor, 0x3e4549, { roughness: 1 });
    this.addBoxes(rocks, 0x202a30, { roughness: 0.98 });
  }

  renderBiome(world, request, presentation) {
    const palette = BIOME_PALETTES[world.worldType] || BIOME_PALETTES.Forest;
    if (presentation.geometryMode === "Surface") {
      this.addGridSurface(
        world,
        (x, y) => world.cells[y * world.width + x] === 1 ? 0.03 : world.elevations[y * world.width + x] || 0,
        (x, y) => this.biomeCellColor(world.cells[y * world.width + x], palette)
      );
      if (presentation.propsEnabled) {
        if (world.worldType === "City") this.renderBuildings(world, palette);
        this.renderBiomeProps(world, request, presentation);
      }
      return;
    }
    const terrainGroups = Array.from({ length: 6 }, () => []);
    const overlays = Array.from({ length: 6 }, () => []);
    for (let y = 0; y < world.height; y++) {
      for (let x = 0; x < world.width; x++) {
        const index = y * world.width + x;
        const cell = world.cells[index];
        const elevation = world.elevations[index] || 0;
        const position = this.cellPosition(world, x, y);
        const voxelBuilding = cell === 4;
        const baseHeight = voxelBuilding ? Math.max(1, world.structureHeights[index] || 1) : Math.max(0.2, elevation + 0.35);
        terrainGroups[cell].push({ x: position.x, y: voxelBuilding ? elevation + baseHeight / 2 : elevation - baseHeight / 2, z: position.z, width: 1.01, height: baseHeight, depth: 1.01 });
        if (cell === 1) overlays[cell].push({ x: position.x, y: 0.025, z: position.z, width: 1.015, height: 0.08, depth: 1.015 });
        if (cell === 2 || cell === 3) overlays[cell].push({ x: position.x, y: elevation + 0.035, z: position.z, width: 1.01, height: 0.07, depth: 1.01 });
      }
    }

    this.addBoxes(terrainGroups[0], palette.ground, { roughness: 0.96 });
    this.addBoxes(terrainGroups[1], this.darken(palette.ground, 0.58), { roughness: 1 });
    this.addBoxes(terrainGroups[2], palette.ground, { roughness: 0.96 });
    this.addBoxes(terrainGroups[3], palette.ground, { roughness: 0.96 });
    this.addBoxes(terrainGroups[4], palette.building, { roughness: 0.96 });
    this.addBoxes(terrainGroups[5], palette.park, { roughness: 0.96 });
    this.addBoxes(overlays[1], palette.water, { roughness: 0.18, metalness: 0.08, transparent: true, opacity: 0.72 });
    this.addBoxes(overlays[2], palette.path, { roughness: 1 });
    this.addBoxes(overlays[3], palette.road, { roughness: 0.92 });
    if (presentation.propsEnabled) {
      this.renderBiomeProps(world, request, presentation);
    }
  }

  renderBuildings(world, palette) {
    const rectangles = this.collectBuildingRectangles(world);
    const entries = rectangles.map(rectangle => {
      const centerX = rectangle.x + rectangle.width / 2 - 0.5;
      const centerY = rectangle.y + rectangle.depth / 2 - 0.5;
      const position = this.cellPosition(world, centerX, centerY);
      const elevation = world.elevations[rectangle.y * world.width + rectangle.x] || 0;
      return {
        x: position.x,
        y: elevation + rectangle.height / 2,
        modelY: elevation,
        z: position.z,
        width: Math.max(0.72, rectangle.width - 0.16),
        height: rectangle.height,
        depth: Math.max(0.72, rectangle.depth - 0.16)
      };
    });
    const procedural = [];
    const tiers = new Map();
    for (const entry of entries) {
      const tier = entry.height < 8 ? "LowRise" : entry.height < 14 ? "MidRise" : "HighRise";
      const key = `City/Building/${tier}`;
      if (!tiers.has(key)) tiers.set(key, []);
      tiers.get(key).push(entry);
    }
    for (const [key, values] of tiers) {
      if (!this.renderModelPool(key, values, entry => [entry.width, entry.height, entry.depth])) procedural.push(...values);
    }
    this.addBoxes(procedural, palette.building, { roughness: 0.72, metalness: 0.08 });
  }

  collectBuildingRectangles(world) {
    const visited = new Uint8Array(world.cells.length);
    const rectangles = [];
    for (let y = 0; y < world.height; y++) {
      for (let x = 0; x < world.width; x++) {
        const start = y * world.width + x;
        if (visited[start] || world.cells[start] !== 4) continue;
        const height = Math.max(1, world.structureHeights[start] || 1);
        let width = 1;
        while (x + width < world.width) {
          const index = y * world.width + x + width;
          if (visited[index] || world.cells[index] !== 4 || Math.abs((world.structureHeights[index] || 1) - height) > 0.01) break;
          width++;
        }
        let depth = 1;
        outer: while (y + depth < world.height) {
          for (let offset = 0; offset < width; offset++) {
            const index = (y + depth) * world.width + x + offset;
            if (visited[index] || world.cells[index] !== 4 || Math.abs((world.structureHeights[index] || 1) - height) > 0.01) break outer;
          }
          depth++;
        }
        for (let row = 0; row < depth; row++) for (let column = 0; column < width; column++) visited[(y + row) * world.width + x + column] = 1;
        rectangles.push({ x, y, width, depth, height, semanticIndex: rectangles.length });
      }
    }
    return rectangles;
  }

  renderBiomeProps(world, request, presentation = { geometryMode: "Surface", propsEnabled: true }) {
    const visual = request?.visualSettings?.trees ?? { density: 1, maxCount: 0, allowedTypes: [] };
    const density = Math.max(0, Math.min(1, visual.density ?? 1));
    const maximum = visual.maxCount > 0 ? visual.maxCount : Number.POSITIVE_INFINITY;
    const selected = [];
    for (const prop of world.props || []) {
      if (selected.length >= maximum) break;
      if (this.stableUnit(world.seed, prop.x, prop.y, 507) > density) continue;
      const position = this.cellPosition(world, prop.x, prop.y);
      const elevation = world.elevations[prop.y * world.width + prop.x] || 0;
      const scale = 0.72 + this.stableUnit(world.seed, prop.x, prop.y, 509) * 0.54;
      selected.push({ ...position, y: elevation, scale, semanticIndex: prop.semanticIndex });
    }

    if (presentation.geometryMode === "Voxel") return this.renderVoxelBiomeProps(world, selected);

    const modelKey = `${world.worldType}/${world.worldType === "City" ? "CityPropAnchor" : "Vegetation"}`;
    if (this.renderModelPool(modelKey, selected, (point, model) => {
      const authoredSize = Array.isArray(model.entry.size) ? model.entry.size : [1.4, 2.6, 1.4];
      return authoredSize.map(value => value * point.scale);
    })) return;
    if (world.worldType === "Desert") return this.renderCacti(selected);
    if (world.worldType === "Snowfield") return this.renderConifers(selected, true);
    if (world.worldType === "Swamp") return this.renderSwampTrees(selected);
    if (world.worldType === "City") return this.renderCityAnchors(selected);
    const cherryOnly = visual.allowedTypes?.length === 1 && visual.allowedTypes[0] === "cherry_blossom";
    return this.renderForestTrees(selected, cherryOnly);
  }

  renderVoxelBiomeProps(world, points) {
    if (world.worldType === "City") {
      return this.addBoxes(points.map(point => ({
        ...point, y: point.y + 0.58 * point.scale, width: 0.46 * point.scale, height: 1.16 * point.scale, depth: 0.46 * point.scale
      })), 0x4d7952, { roughness: 0.94 });
    }
    const colors = {
      Desert: { trunk: 0x4f8249, crown: 0x6e9d51 },
      Snowfield: { trunk: 0x5b4735, crown: 0xaac5b7 },
      Swamp: { trunk: 0x4c3c2e, crown: 0x536f48 },
      Forest: { trunk: 0x5f422b, crown: 0x4e8050 }
    };
    const palette = colors[world.worldType] || colors.Forest;
    this.addBoxes(points.map(point => ({
      ...point, y: point.y + 0.5 * point.scale, width: 0.26 * point.scale, height: 1.0 * point.scale, depth: 0.26 * point.scale
    })), palette.trunk, { roughness: 0.96 });
    this.addBoxes(points.map(point => ({
      ...point, y: point.y + 1.28 * point.scale, width: 1.02 * point.scale, height: 0.82 * point.scale, depth: 1.02 * point.scale
    })), palette.crown, { roughness: 0.95 });
  }

  renderForestTrees(points, cherryOnly) {
    this.addCylinders(points.map(p => ({ ...p, y: p.y + 0.72 * p.scale })), 0.13, 1.44, 0x5f422b, 7, false, p => p.scale);
    this.addCones(points.map(p => ({ ...p, y: p.y + 1.65 * p.scale })), 0.66, 1.45, cherryOnly ? 0xf2a8bf : 0x4e8050, 8, 0, false, p => p.scale);
  }

  renderConifers(points, snowy) {
    this.addCylinders(points.map(p => ({ ...p, y: p.y + 0.55 * p.scale })), 0.11, 1.1, 0x5b4735, 7, false, p => p.scale);
    this.addCones(points.map(p => ({ ...p, y: p.y + 1.5 * p.scale })), 0.68, 2.05, snowy ? 0xaac5b7 : 0x3f7049, 9, 0, false, p => p.scale);
  }

  renderSwampTrees(points) {
    this.addCylinders(points.map(p => ({ ...p, y: p.y + 0.85 * p.scale })), 0.16, 1.7, 0x4c3c2e, 7, false, p => p.scale);
    this.addSpheres(points.map(p => ({ ...p, y: p.y + 1.75 * p.scale })), 0.58, 0x536f48, false, p => p.scale);
  }

  renderCacti(points) {
    this.addCylinders(points.map(p => ({ ...p, y: p.y + 0.68 * p.scale })), 0.2, 1.36, 0x4f8249, 8, false, p => p.scale);
  }

  renderCityAnchors(points) {
    this.addCylinders(points.map(p => ({ ...p, y: p.y + 0.4 * p.scale })), 0.09, 0.8, 0x4e3d2b, 7, false, p => p.scale);
    this.addSpheres(points.map(p => ({ ...p, y: p.y + 0.95 * p.scale })), 0.38, 0x4d7952, false, p => p.scale);
  }

  renderModelPool(key, entries, sizeSelector) {
    const pool = this.modelPools.get(key) || [];
    if (!pool.length || !entries.length) return false;
    const buckets = Array.from({ length: pool.length }, () => []);
    for (const entry of entries) buckets[Math.abs(entry.semanticIndex || 0) % pool.length].push(entry);
    pool.forEach((model, index) => this.addModelInstances(model, buckets[index], sizeSelector));
    this.usedModelAssets = true;
    return true;
  }

  addModelInstances(model, entries, sizeSelector) {
    if (!entries.length) return;
    const position = new THREE.Vector3();
    const scale = new THREE.Vector3();
    const rotation = new THREE.Quaternion();
    const placement = new THREE.Matrix4();
    const finalMatrix = new THREE.Matrix4();
    for (const primitive of model.primitives) {
      const geometry = primitive.geometry.clone();
      const material = Array.isArray(primitive.material) ? primitive.material.map(value => value.clone()) : primitive.material.clone();
      const mesh = new THREE.InstancedMesh(geometry, material, entries.length);
      mesh.instanceMatrix.setUsage(THREE.StaticDrawUsage);
      entries.forEach((entry, index) => {
        const desiredSize = sizeSelector(entry, model);
        position.set(entry.x, entry.modelY ?? entry.y ?? 0, entry.z);
        scale.set(desiredSize[0], desiredSize[1], desiredSize[2]);
        rotation.setFromAxisAngle(new THREE.Vector3(0, 1, 0), this.stableUnit(this.world.seed, entry.semanticIndex || index, index, 811) * Math.PI * 2);
        placement.compose(position, rotation, scale);
        finalMatrix.copy(placement).multiply(primitive.matrix);
        mesh.setMatrixAt(index, finalMatrix);
      });
      mesh.instanceMatrix.needsUpdate = true;
      mesh.castShadow = entries.length < 5000;
      mesh.receiveShadow = true;
      this.worldRoot.add(mesh);
    }
  }

  addMarkers(world) {
    for (const [point, color] of [[world.start, 0x4cf09a], [world.exit, 0xff646d]]) {
      if (!point) continue;
      const position = this.cellPosition(world, point.x, point.y);
      const index = point.y * world.width + point.x;
      const elevation = world.elevations[index] || 0;
      this.addCylinders([{ ...position, y: elevation + 0.08 }], 0.32, 0.16, color, 20, true);
      this.addSpheres([{ ...position, y: elevation + 0.55 }], 0.11, color, true);
    }
  }

  addWorldFrame(world) {
    const frame = new THREE.LineSegments(
      new THREE.EdgesGeometry(new THREE.BoxGeometry(world.width + 0.5, 0.08, world.height + 0.5)),
      new THREE.LineBasicMaterial({ color: 0x5f8090, transparent: true, opacity: 0.32 })
    );
    frame.position.y = -0.22;
    this.worldRoot.add(frame);
  }

  addFlatTiles(entries, color, options = {}) {
    if (!entries?.length) return null;
    const positions = [];
    for (const entry of entries) {
      const halfWidth = (entry.width || 1) * 0.5;
      const halfDepth = (entry.depth || 1) * 0.5;
      const y = entry.y || 0;
      const a = [entry.x - halfWidth, y, entry.z - halfDepth];
      const b = [entry.x + halfWidth, y, entry.z - halfDepth];
      const c = [entry.x + halfWidth, y, entry.z + halfDepth];
      const d = [entry.x - halfWidth, y, entry.z + halfDepth];
      positions.push(...a, ...d, ...c, ...a, ...c, ...b);
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
    geometry.computeVertexNormals();
    const material = new THREE.MeshStandardMaterial({ color, roughness: options.roughness ?? 0.9, metalness: options.metalness ?? 0 });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.receiveShadow = true;
    this.worldRoot.add(mesh);
    return mesh;
  }

  addGridSurface(world, heightSelector, colorSelector) {
    const positions = [];
    const colors = [];
    const cornerHeight = (cornerX, cornerY) => {
      let total = 0;
      let count = 0;
      for (let offsetY = -1; offsetY <= 0; offsetY++) for (let offsetX = -1; offsetX <= 0; offsetX++) {
        const x = cornerX + offsetX;
        const y = cornerY + offsetY;
        if (x < 0 || y < 0 || x >= world.width || y >= world.height) continue;
        total += heightSelector(x, y);
        count++;
      }
      return count ? total / count : 0;
    };
    for (let y = 0; y < world.height; y++) {
      for (let x = 0; x < world.width; x++) {
        const x0 = x - world.width / 2;
        const x1 = x0 + 1;
        const z0 = y - world.height / 2;
        const z1 = z0 + 1;
        const a = [x0, cornerHeight(x, y), z0];
        const b = [x1, cornerHeight(x + 1, y), z0];
        const c = [x1, cornerHeight(x + 1, y + 1), z1];
        const d = [x0, cornerHeight(x, y + 1), z1];
        positions.push(...a, ...d, ...c, ...a, ...c, ...b);
        const color = new THREE.Color(colorSelector(x, y));
        for (let vertex = 0; vertex < 6; vertex++) colors.push(color.r, color.g, color.b);
      }
    }
    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
    geometry.setAttribute("color", new THREE.Float32BufferAttribute(colors, 3));
    geometry.computeVertexNormals();
    const material = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.92, metalness: 0 });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.castShadow = world.width * world.height < 20000;
    mesh.receiveShadow = true;
    this.worldRoot.add(mesh);
    return mesh;
  }

  biomeCellColor(cell, palette) {
    if (cell === 1) return palette.water;
    if (cell === 2) return palette.path;
    if (cell === 3) return palette.road;
    if (cell === 4) return this.darken(palette.ground, 0.82);
    if (cell === 5) return palette.park;
    return palette.ground;
  }

  addBoxes(entries, color, options = {}) {
    if (!entries?.length) return null;
    return this.addInstanced(new THREE.BoxGeometry(1, 1, 1), entries, color, options, entry => ({
      position: [entry.x, entry.y, entry.z],
      rotation: [0, entry.yaw || 0, 0],
      scale: [entry.width || 1, entry.height || 1, entry.depth || 1]
    }));
  }

  addCylinders(entries, radius, height, color, sides = 8, emissive = false, scaleSelector = null) {
    if (!entries?.length) return null;
    return this.addInstanced(new THREE.CylinderGeometry(radius, radius * 1.08, height, sides), entries, color, { emissive }, entry => {
      const scale = scaleSelector ? scaleSelector(entry) : 1;
      return { position: [entry.x, entry.y ?? height / 2, entry.z], rotation: [0, 0, 0], scale: [scale, scale, scale] };
    });
  }

  addCones(entries, radius, height, color, sides = 8, yOffset = 0, emissive = false, scaleSelector = null) {
    if (!entries?.length) return null;
    return this.addInstanced(new THREE.ConeGeometry(radius, height, sides), entries, color, { emissive }, entry => {
      const scale = scaleSelector ? scaleSelector(entry) : 1;
      return { position: [entry.x, (entry.y ?? height / 2) + yOffset, entry.z], rotation: [0, 0, 0], scale: [scale, scale, scale] };
    });
  }

  addSpheres(entries, radius, color, emissive = false, scaleSelector = null) {
    if (!entries?.length) return null;
    return this.addInstanced(new THREE.IcosahedronGeometry(radius, 1), entries, color, { emissive }, entry => {
      const scale = scaleSelector ? scaleSelector(entry) : 1;
      return { position: [entry.x, entry.y ?? radius, entry.z], rotation: [0, 0, 0], scale: [scale, scale, scale] };
    });
  }

  addInstanced(geometry, entries, color, options, transform) {
    const material = new THREE.MeshStandardMaterial({
      color,
      roughness: options.roughness ?? 0.82,
      metalness: options.metalness ?? 0,
      transparent: options.transparent ?? false,
      opacity: options.opacity ?? 1,
      emissive: options.emissive ? color : 0x000000,
      emissiveIntensity: options.emissive ? 1.6 : 0
    });
    const mesh = new THREE.InstancedMesh(geometry, material, entries.length);
    mesh.instanceMatrix.setUsage(THREE.StaticDrawUsage);
    mesh.castShadow = entries.length < 12000 && !options.transparent;
    mesh.receiveShadow = !options.transparent;
    const helper = new THREE.Object3D();
    entries.forEach((entry, index) => {
      const value = transform(entry);
      helper.position.set(...value.position);
      helper.rotation.set(...value.rotation);
      helper.scale.set(...value.scale);
      helper.updateMatrix();
      mesh.setMatrixAt(index, helper.matrix);
    });
    mesh.instanceMatrix.needsUpdate = true;
    this.worldRoot.add(mesh);
    return mesh;
  }

  cellPosition(world, x, y) {
    return { x: x - world.width / 2 + 0.5, z: y - world.height / 2 + 0.5 };
  }

  stableUnit(seed, x, y, salt) {
    let value = (seed ^ Math.imul(x + salt, 73856093) ^ Math.imul(y - salt, 19349663)) >>> 0;
    value ^= value >>> 16;
    value = Math.imul(value, 0x7feb352d) >>> 0;
    value ^= value >>> 15;
    return (value & 0xffffff) / 0xffffff;
  }

  darken(color, factor) {
    const value = new THREE.Color(color);
    value.multiplyScalar(factor);
    return value.getHex();
  }

  fitCamera(view = "isometric") {
    if (!this.world) return;
    const size = Math.max(this.world.width, this.world.height);
    const maxElevation = this.maximumValue(this.world.elevations, 2);
    const maxStructure = this.maximumValue(this.world.structureHeights, 0);
    const height = Math.max(4, maxElevation + maxStructure);
    this.controls.target.set(0, height * 0.22, 0);
    if (view === "top") this.camera.position.set(0.001, size * 1.18 + height, 0.001);
    else if (view === "ground") this.camera.position.set(0, Math.max(3, height * 0.3), size * 0.48);
    else this.camera.position.set(size * 0.62, size * 0.68 + height * 0.5, size * 0.62);
    this.camera.near = Math.max(0.05, size / 3000);
    this.camera.far = Math.max(800, size * 8);
    this.camera.updateProjectionMatrix();
    this.controls.update();
  }

  clearWorld() {
    const previous = this.worldRoot;
    this.scene.remove(previous);
    previous.traverse(object => {
      object.geometry?.dispose?.();
      if (Array.isArray(object.material)) object.material.forEach(material => material.dispose());
      else object.material?.dispose?.();
    });
    this.worldRoot = new THREE.Group();
    this.worldRoot.name = "GeneratedWorld";
    this.scene.add(this.worldRoot);
  }

  maximumValue(values, fallback) {
    if (!values?.length) return fallback;
    let maximum = values[0];
    for (let index = 1; index < values.length; index++) if (values[index] > maximum) maximum = values[index];
    return maximum;
  }

  resize() {
    const width = Math.max(1, this.container.clientWidth);
    const height = Math.max(1, this.container.clientHeight);
    this.camera.aspect = width / height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(width, height, false);
  }

  animate() {
    this.controls.update();
    this.renderer.render(this.scene, this.camera);
    this.animationFrame = requestAnimationFrame(this.animate);
  }

  dispose() {
    cancelAnimationFrame(this.animationFrame);
    this.resizeObserver.disconnect();
    this.clearWorld();
    this.controls.dispose();
    this.renderer.dispose();
  }
}
