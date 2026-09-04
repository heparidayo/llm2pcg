/** Node-side guard and legacy upgrader. Unity remains authoritative. */
const WORLD_CONTRACTS = {
  Dungeon: { version:"dungeon-bsp@1", biome:"Stone", profiles:["DefaultDungeon","CompactDungeon","SprawlingDungeon"], branch:"dungeon" },
  Cave: { version:"cave-cellular@1", biome:"Stone", profiles:["DefaultCave","CavernousCave","TightCave"], branch:"cave" },
  Forest: { version:"forest-biome@1", biome:"Temperate", profiles:["DefaultForest","DenseForest","MeadowForest"], branch:"forest" },
  City: { version:"city-hybrid-wfc@1", biome:"Temperate", profiles:["DefaultCity","GridCity","OrganicCity"], branch:"city" },
  Swamp: { version:"swamp-biome@1", biome:"Wetland", profiles:["DefaultSwamp","OpenMarsh","DenseBog"], branch:"forest" },
  Snowfield: { version:"snowfield-biome@1", biome:"Arctic", profiles:["DefaultSnowfield","SparseTundra","FrozenGrove"], branch:"forest" },
  Desert: { version:"desert-biome@1", biome:"Arid", profiles:["DefaultDesert","DuneSea","OasisDesert"], branch:"forest" }
};

const NATURE_DEFAULTS = {
  Swamp: { waterThreshold:.46,noiseOctaves:4,clearingRadius:12,vegetationMinDistance:4.5,vegetationMaxCount:500,elevationScale:3.5,elevationFrequency:.03 },
  Snowfield: { waterThreshold:.18,noiseOctaves:4,clearingRadius:16,vegetationMinDistance:6,vegetationMaxCount:320,elevationScale:8,elevationFrequency:.025 },
  Desert: { waterThreshold:.08,noiseOctaves:4,clearingRadius:18,vegetationMinDistance:8,vegetationMaxCount:180,elevationScale:10,elevationFrequency:.018 }
};

export function upgradePcgRequest(input) {
  if (!input || typeof input !== "object" || Array.isArray(input)) return input;
  if (input.schemaVersion === 3) return { ...input, visualSettings:input.visualSettings??defaultVisuals(), presentationSettings:normalizePresentation(input.presentationSettings), layoutSettings:normalizeLayout(input.layoutSettings) };
  if (input.schemaVersion === 2) return { ...input, schemaVersion:3, visualSettings:input.visualSettings??defaultVisuals(), presentationSettings:normalizePresentation(input.presentationSettings), layoutSettings:normalizeLayout(input.layoutSettings) };
  if (input.schemaVersion !== 1 || input.worldType !== "Dungeon") return input;
  return { ...input, schemaVersion:3, generatorVersion:"dungeon-bsp@1", generatorSettings:{ dungeon:input.generatorSettings?.dungeon ?? {maxDepth:5,minLeafSize:10},city:null,forest:null,cave:null }, propSettings:input.propSettings ?? disabledProps(), visualSettings:input.visualSettings ?? defaultVisuals(), presentationSettings:normalizePresentation(input.presentationSettings), layoutSettings:defaultLayout(), specialRooms:input.specialRooms ?? disabledRooms() };
}

export function validatePcgRequest(input) {
  const r=upgradePcgRequest(input);
  if(!r||typeof r!=="object"||Array.isArray(r))return "Request must be a JSON object.";
  if(r.schemaVersion!==3)return "schemaVersion must be 1, 2, or 3.";
  if(!Number.isInteger(r.seed)||!Number.isInteger(r.mapWidth)||!Number.isInteger(r.mapHeight)||r.mapWidth<1||r.mapHeight<1)return "seed and map dimensions are invalid.";
  const contract=WORLD_CONTRACTS[r.worldType]; if(!contract)return "Unsupported worldType.";
  if(r.generatorVersion!==contract.version)return "generatorVersion does not match worldType.";
  if(r.biome!==contract.biome||!contract.profiles.includes(r.generationProfile))return "biome or generationProfile does not match worldType.";
  if(!r.generatorSettings||typeof r.generatorSettings!=="object")return "generatorSettings is required.";
  const active=["dungeon","cave","forest","city"].filter(k=>r.generatorSettings[k]!=null);
  if(active.length!==1||active[0]!==contract.branch)return "Exactly one matching generatorSettings branch is required.";
  const p=r.propSettings; if(!p||typeof p.enabled!=="boolean"||typeof p.density!=="number"||p.density<0||p.density>1||!Number.isInteger(p.maxCount)||!Array.isArray(p.allowedTypes))return "propSettings is invalid.";
  const v=r.visualSettings;if(!v||![v.trees,v.rocks,v.bushes,v.groundDetails,v.waterProps].every(validVisualCategory))return "visualSettings is invalid.";
  const presentation=r.presentationSettings;if(!presentation||!["Voxel","Surface"].includes(presentation.geometryMode)||typeof presentation.propsEnabled!=="boolean")return "presentationSettings is invalid.";
  if(!validLayout(r.layoutSettings))return "layoutSettings is invalid.";
  if(r.worldType==="Dungeon"){const s=r.generatorSettings.dungeon;if(!s||s.maxDepth<1||s.minLeafSize<1||s.minLeafSize>=r.mapWidth||s.minLeafSize>=r.mapHeight)return "Dungeon settings are invalid.";for(const k of ["boss","treasure","shop","secret"])if(!r.specialRooms?.[k])return "Dungeon specialRooms are required.";}
  if(r.worldType==="Cave"){const s=r.generatorSettings.cave;if(!s||s.fillPercent<20||s.fillPercent>80||s.automataSteps<1||s.minimumRegionSize<1||s.tunnelRadius<1||s.extraTunnelCount<0)return "Cave settings are invalid.";}
  if(r.worldType==="Forest"||r.worldType==="Swamp"||r.worldType==="Snowfield"||r.worldType==="Desert"){const s=r.generatorSettings.forest;if(!s||s.waterThreshold<0||s.waterThreshold>1||s.noiseOctaves<1||s.noiseOctaves>8||s.clearingRadius<=0||s.vegetationMinDistance<=0||s.vegetationMaxCount<0||s.elevationScale<=0||s.elevationScale>64||s.elevationFrequency<=0||s.elevationFrequency>1)return "Nature settings are invalid.";}
  if(r.worldType==="City"){const s=r.generatorSettings.city;if(!s||s.hubMinDistance<=0||s.hubMaxCount<2||s.roadWidth<1||s.extraLoopCount<0||s.wfcBacktrackLimit<0||s.wfcRestartLimit<0||s.buildingMinHeight<1||s.buildingMaxHeight<s.buildingMinHeight||s.buildingMaxHeight>128)return "City settings are invalid.";}
  return null;
}

export function defaultRequest(worldType="Dungeon",seed=12345,mapWidth=100,mapHeight=100){
  const contract=WORLD_CONTRACTS[worldType]??WORLD_CONTRACTS.Dungeon;
  const selected=WORLD_CONTRACTS[worldType]?worldType:"Dungeon";
  const common={schemaVersion:3,seed,mapWidth,mapHeight,worldType:selected,biome:contract.biome,generationProfile:contract.profiles[0],generatorVersion:contract.version,propSettings:disabledProps(),visualSettings:defaultVisuals(),presentationSettings:defaultPresentation(),layoutSettings:defaultLayout(),specialRooms:null};
  if(selected==="Cave")return {...common,generatorSettings:{dungeon:null,city:null,forest:null,cave:{fillPercent:46,automataSteps:5,minimumRegionSize:24,tunnelRadius:1,extraTunnelCount:1}}};
  if(selected==="Forest")return {...common,generatorSettings:{dungeon:null,city:null,forest:{waterThreshold:.20,noiseOctaves:4,clearingRadius:14,vegetationMinDistance:4,vegetationMaxCount:600,elevationScale:6,elevationFrequency:.035},cave:null}};
  if(selected==="Swamp"||selected==="Snowfield"||selected==="Desert")return {...common,generatorSettings:{dungeon:null,city:null,forest:{...NATURE_DEFAULTS[selected]},cave:null}};
  if(selected==="City")return {...common,generatorSettings:{dungeon:null,city:{hubMinDistance:20,hubMaxCount:8,roadWidth:2,extraLoopCount:2,wfcBacktrackLimit:64,wfcRestartLimit:2,buildingMinHeight:4,buildingMaxHeight:16},forest:null,cave:null}};
  return {...common,generatorSettings:{dungeon:{maxDepth:5,minLeafSize:10},city:null,forest:null,cave:null},specialRooms:disabledRooms()};
}

function disabledProps(){return {enabled:false,density:0,maxCount:0,allowedTypes:[]};}
function defaultVisualCategory(){return {density:1,maxCount:0,allowedTypes:[]};}
function defaultVisuals(){return {trees:defaultVisualCategory(),rocks:defaultVisualCategory(),bushes:defaultVisualCategory(),groundDetails:defaultVisualCategory(),waterProps:defaultVisualCategory()};}
function defaultPresentation(){return {geometryMode:"Surface",propsEnabled:true};}
function normalizePresentation(value){return value&&typeof value.geometryMode==="string"?value:defaultPresentation();}
function defaultLayout(){return {landform:{kind:"None",placement:"Seeded",size:"Medium",intensity:"Medium"},water:{kind:"Default",placement:"Seeded",size:"Medium",meander:"Low",exclusive:false},route:{kind:"Default",placement:"Seeded",size:"Medium",orientation:"Seeded",meander:"Low"}};}
function normalizeLayout(value){const defaults=defaultLayout();return {landform:value?.landform??defaults.landform,water:value?.water??defaults.water,route:value?.route??defaults.route};}
function validVisualCategory(value){return value&&typeof value.density==="number"&&value.density>=0&&value.density<=1&&Number.isInteger(value.maxCount)&&value.maxCount>=0&&Array.isArray(value.allowedTypes)&&value.allowedTypes.every(type=>VISUAL_TYPES.has(type));}
function validLayout(value){return value&&value.landform&&value.water&&value.route&&value.landform.kind==="None"&&value.landform.placement==="Seeded"&&value.landform.size==="Medium"&&value.landform.intensity==="Medium"&&value.water.kind==="Default"&&value.water.placement==="Seeded"&&value.water.size==="Medium"&&value.water.meander==="Low"&&value.water.exclusive===false&&value.route.kind==="Default"&&value.route.placement==="Seeded"&&value.route.size==="Medium"&&value.route.orientation==="Seeded"&&value.route.meander==="Low";}
const VISUAL_TYPES=new Set(["cherry_blossom","broadleaf","conifer","willow","dead_tree","palm","cactus","rock","bush","reeds","grass","flower","plant","mushroom","stump","log","branch","thorn","lily_pad","water_lily","ice"]);
function disabledRooms(){return {boss:{enabled:false,count:0,placement:"FarthestFromStart"},treasure:{enabled:false,count:0,placement:"FarthestFromStart"},shop:{enabled:false,count:0,placement:"FarthestFromStart"},secret:{enabled:false,count:0,placement:"FarthestFromStart"}};}
