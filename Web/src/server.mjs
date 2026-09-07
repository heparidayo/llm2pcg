import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { upgradePcgRequest, validatePcgRequest } from "../../Shared/pcg-request.mjs";
import { analyzePrompt, applyExplicitNumbers, applyVisualIntent, explicitPropsIntent } from "./prompt-intent.mjs";

const moduleDirectory = path.dirname(fileURLToPath(import.meta.url));
const publicDirectory = path.resolve(moduleDirectory, "../public");
const unityPublicDirectory = path.join(publicDirectory, "unity");
const threeDirectory = path.resolve(moduleDirectory, "../node_modules/three");
const sharedDirectory = path.resolve(moduleDirectory, "../../Shared");
const environmentFile = path.resolve(moduleDirectory, "../.env");
const localAssetManifest = path.join(publicDirectory, "asset-manifest.local.json");
const defaultAssetManifest = path.join(publicDirectory, "asset-manifest.json");
const modelDirectory = path.join(publicDirectory, "assets/models");

await loadEnvironmentFile(environmentFile);

const requestSchema = JSON.parse(await readFile(path.resolve(moduleDirectory, "../../Shared/Schema/pcg-request.schema.json"), "utf8"));
const openAiRequestSchema = toOpenAiStrictSchema(requestSchema);
const coreEndpoint = process.env.PCG_CORE_ENDPOINT ?? "http://127.0.0.1:8090/api/world/generate";
const unityEndpoint = process.env.UNITY_PCG_ENDPOINT ?? "http://127.0.0.1:8088/pcg/generate";
const openAiModel = process.env.OPENAI_MODEL ?? "gpt-5.4-mini";

export { validatePcgRequest };

async function loadEnvironmentFile(filePath) {
  let contents;
  try {
    contents = await readFile(filePath, "utf8");
  } catch (error) {
    if (error.code === "ENOENT") return;
    throw error;
  }

  for (const rawLine of contents.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#")) continue;
    const separator = line.indexOf("=");
    if (separator <= 0) continue;
    const key = line.slice(0, separator).trim();
    let value = line.slice(separator + 1).trim();
    if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) value = value.slice(1, -1);
    if (process.env[key] === undefined) process.env[key] = value;
  }
}

// Responses strict mode supports a JSON Schema subset. Derive the compatible form from the shared source,
// keeping frontend and generator-core contract values in one authored schema.
function toOpenAiStrictSchema(value) {
  if (Array.isArray(value)) return value.map(toOpenAiStrictSchema);
  if (value === null || typeof value !== "object") return value;
  const result = {};
  for (const [key, child] of Object.entries(value)) {
    if (key === "$schema" || key === "$id" || key === "title") continue;
    if (key === "const") {
      result.enum = [child];
    } else {
      result[key] = toOpenAiStrictSchema(child);
    }
  }
  return result;
}

export function extractOutputText(response) {
  if (response?.status && response.status !== "completed")
    throw Object.assign(new Error("OpenAI response did not complete."), {code:"OPENAI_INCOMPLETE_RESPONSE"});
  for (const item of response?.output ?? [])
    for (const content of item.content ?? [])
      if(content.type==="refusal")
        throw Object.assign(new Error("OpenAI declined to translate this request."), {code:"OPENAI_REFUSAL"});
  if (typeof response.output_text === "string" && response.output_text.length > 0) return response.output_text;
  for (const item of response.output ?? []) {
    for (const content of item.content ?? []) {
      if (content.type === "output_text" && typeof content.text === "string") return content.text;
    }
  }
  throw new Error("OpenAI response did not contain output text.");
}

// Presentation mode is renderer-only, so explicit Korean/English mode wording can be
// made deterministic after the semantic translation without touching world generation.
export function applyPresentationIntent(prompt, request) {
  const text = String(prompt ?? "").normalize("NFKC").toLowerCase();
  const presentation = request.presentationSettings ?? { geometryMode: "Surface", propsEnabled: true };
  const pureVoxelPreset = /(순수\s*복셀|복셀\s*(?:형|스타일)|pure\s*voxel|voxel\s*(?:style|with\s*voxel\s*props?))/i.test(text);
  const naturalTerrainWithAssetsPreset = /(자연(?:스러운)?\s*(?:지형|terrain)[^.!?\n]{0,22}(?:프롭|모델|에셋)[^.!?\n]{0,22}(?:추가|포함|사용|배치|있)|(?:프롭|모델|에셋)[^.!?\n]{0,22}자연(?:스러운)?\s*(?:지형|terrain))/i.test(text);
  const naturalTerrainWithoutPropsPreset = /(자연(?:스러운)?\s*(?:지형|terrain)[^.!?\n]{0,22}(?:프롭|모델|에셋)[^.!?\n]{0,22}(?:없|빼|제외|안\s*들어|미사용)|(?:프롭|모델|에셋)[^.!?\n]{0,22}(?:없|빼|제외|미사용)[^.!?\n]{0,22}자연(?:스러운)?\s*(?:지형|terrain))/i.test(text);
  const completePreset = /(완성형|최종(?:형|본| 버전)?|finished|complete(?:d)?)/i.test(text);
  const voxelPreset = /(초기\s*복셀|복셀\s*(?:만|버전|단계)|voxel\s*(?:only|version|stage))/i.test(text);
  const voxelWithPropsPreset = /(프롭\s*\+\s*복셀|복셀\s*\+\s*프롭|프롭[^.!?\n]{0,12}(?:들어|포함|있는|사용)[^.!?\n]{0,12}복셀|voxel\s*with\s*props)/i.test(text);
  const surfaceWithoutPropsPreset = /((?:비|논)\s*복셀[^.!?\n]{0,16}(?:표면|프롭|모델링)|(?:표면|surface)[^.!?\n]{0,16}(?:만|프롭\s*없|모델링\s*없)|surface\s*(?:only|without\s*props))/i.test(text);
  const noProps = explicitPropsIntent(prompt) === false;
  const withProps = explicitPropsIntent(prompt) === true;
  const hasExplicitPresentationIntent = pureVoxelPreset || naturalTerrainWithAssetsPreset || naturalTerrainWithoutPropsPreset || completePreset || voxelPreset || voxelWithPropsPreset || surfaceWithoutPropsPreset || noProps || withProps;

  // A model response without an explicit presentation instruction must never turn
  // a normal natural-language world into an empty, prop-free preview.
  if (!hasExplicitPresentationIntent) {
    presentation.geometryMode = "Surface";
    presentation.propsEnabled = true;
  }

  if (naturalTerrainWithAssetsPreset || completePreset) {
    presentation.geometryMode = "Surface";
    presentation.propsEnabled = true;
  } else if (naturalTerrainWithoutPropsPreset || surfaceWithoutPropsPreset) {
    presentation.geometryMode = "Surface";
    presentation.propsEnabled = false;
  } else if (pureVoxelPreset || voxelWithPropsPreset || voxelPreset) {
    presentation.geometryMode = "Voxel";
    presentation.propsEnabled = true;
  }

  const nonVoxel = /((?:비|논)\s*복셀|복셀(?:은|이)?\s*(?:아닌|아니게|없이)|표면|non[- ]?voxel|surface)/i.test(text);
  const explicitVoxel = /(^|[^비논])복셀|\bvoxel\b/i.test(text);
  if (!completePreset && !naturalTerrainWithAssetsPreset && !naturalTerrainWithoutPropsPreset) {
    if (nonVoxel) presentation.geometryMode = "Surface";
    else if (explicitVoxel) presentation.geometryMode = "Voxel";
    if (noProps) presentation.propsEnabled = false;
    else if (withProps) presentation.propsEnabled = true;
  }

  request.presentationSettings = presentation;
  // A named preset must not override an explicit omission.
  if (noProps) presentation.propsEnabled = false;
  return request;
}

// Keep the established public helper while using target-scoped domain parsing.
export const applyDefaultWorldComponents = applyVisualIntent;

export function applyForestWaterPolicy(prompt, request) {
  if (request?.worldType !== "Forest" || !request.generatorSettings?.forest) return request;
  const text = String(prompt ?? "").normalize("NFKC").toLowerCase();
  const explicit = analyzePrompt(prompt).constraints.some(c=>c.path==="generatorSettings.forest.waterThreshold");
  if (explicit) return applyExplicitNumbers(prompt,request);
  const wet = /(?:물|호수|강|수로)[^.!?\n]{0,12}(?:많|가득|풍부|넘치|투성이|여러)|습한|습윤한|침수된|범람한|늪\s*(?:같은|처럼)|\b(?:wet|water[- ]rich|flooded|swampy)\b/i.test(text);
  const current=request.generatorSettings.forest.waterThreshold;
  if (!Number.isFinite(current)) return request; // Validation must expose malformed model values.
  if (!wet) request.generatorSettings.forest.waterThreshold=Math.min(current,.20);
  return request;
}

export function finalizePcgInterpretation(prompt,candidate) {
  const validationError=validatePcgRequest(candidate);
  if(validationError) throw Object.assign(new Error(validationError),{code:"OPENAI_INVALID_CONTRACT"});
  const request=structuredClone(upgradePcgRequest(candidate));
  applyPresentationIntent(prompt,request);
  applyDefaultWorldComponents(prompt,request);
  applyForestWaterPolicy(prompt,request);
  applyExplicitNumbers(prompt,request);
  const finalError=validatePcgRequest(request);
  if(finalError) throw Object.assign(new Error(finalError),{code:"INVALID_PROMPT_CONSTRAINT"});
  const interpretation=analyzePrompt(prompt);
  return {request,interpretation};
}

export async function requestPcgFromOpenAi(prompt, fetchImplementation = fetch) {
  if (!process.env.OPENAI_API_KEY) {
    const error = new Error("OPENAI_API_KEY is not configured for the Web middleware.");
    error.code = "OPENAI_API_KEY_MISSING";
    throw error;
  }

  const lexicalIntent=analyzePrompt(prompt);
  const response = await fetchImplementation("https://api.openai.com/v1/responses", {
    method: "POST",
    signal: AbortSignal.timeout(60000),
    headers: {
      "Authorization": `Bearer ${process.env.OPENAI_API_KEY}`,
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      model: openAiModel,
      store: false,
      input: [
        {
          role: "developer",
          content: "Translate the user's request into exactly one schemaVersion 3 PCGRequest JSON object. Support Dungeon/Stone/dungeon-bsp@1, Cave/Stone/cave-cellular@1, Forest/Temperate/forest-biome@1, City/Temperate/city-hybrid-wfc@1, Swamp/Wetland/swamp-biome@1, Snowfield/Arctic/snowfield-biome@1, and Desert/Arid/desert-biome@1. Swamp, Snowfield, and Desert reuse exactly generatorSettings.forest; all inactive branches must be null. Choose only a profile valid for the worldType. Use noiseOctaves 3-6 and practical biome ranges. For a normal Forest use waterThreshold 0.20 so water remains a small accent; only exceed 0.20 when the user explicitly asks for a wet, flooded, swamp-like, or water-rich forest. Disable propSettings outside Dungeon. Always emit all visualSettings and layoutSettings fields. Empty visual allowedTypes means unrestricted; density 1 and maxCount 0 mean default/unlimited. Default policy: unless the user explicitly asks to omit or reduce a category, keep every visual category at density 1/maxCount 0. A plain forest must keep all normal visual categories. Map 벚꽃/사쿠라 to trees [cherry_blossom], 활엽수 [broadleaf], 침엽수/소나무 [conifer], 버드나무 [willow], 고사목 [dead_tree], 야자수 [palm], 선인장 [cactus], 바위 to rocks [rock], 덤불 [bush], 갈대 [reeds], and 풀/꽃/식물/버섯/그루터기/통나무/가지/가시덤불 to matching groundDetails. A phrase such as '나무는 벚꽃만' restricts only trees. Interpret 없게 as density 0, 적게 as about .35, 많이 as 1. Use null specialRooms outside Dungeon. Do not explain the JSON or expose algorithms."
        },
        {
          role: "developer",
          content: "Always emit the neutral layoutSettings compatibility value: landform None/Seeded/Medium/Medium, water Default/Seeded/Medium/Low/exclusive false, route Default/Seeded/Medium/Seeded/Low. Spatial placement requests such as a central mountain, central or crossing river, or crossing road are not supported and must not change layoutSettings. 툰드라 -> Snowfield with SparseTundra. 푸릇푸릇/울창 -> Forest with DenseForest, vegetationMinDistance about 3-4, vegetationMaxCount at least 650, and unrestricted visual categories at density 1."
        },
        {
          role: "developer",
          content: "Always emit presentationSettings. It changes rendering only and never the generated world. Default -> Surface,true. 순수 복셀/복셀 버전 -> Voxel,true; the Web renderer draws both terrain and props as voxels. 자연스러운 지형 with 프롭 제외/프롭 없이 -> Surface,false. 자연스러운 지형 with 프롭 에셋 추가, 완성형/최종/finished -> Surface,true. Explicitly omitting props/assets always sets propsEnabled false. When geometry and prop wording are separate, obey both explicitly."
        },
        {
          role:"developer",
          content:"Domain lexer constraints from the user are listed below. Preserve these explicit values; maxCount is an upper bound, not an exact placement guarantee. For all other meaning use the user's description. Capability warnings describe limits, not supported features. Do not turn a subtype exclusion into deleting the whole category.\n"+JSON.stringify({constraints:lexicalIntent.constraints.map(({path,value})=>({path,value})),warnings:lexicalIntent.warnings.map(({code})=>code)})
        },
        { role: "user", content: prompt }
      ],
      text: {
        format: {
          type: "json_schema",
          name: "pcg_request",
          strict: true,
          schema: openAiRequestSchema
        }
      }
    })
  });

  const body = await response.json().catch(() => {
    throw Object.assign(new Error("OpenAI returned a non-JSON response."),{code:"OPENAI_INVALID_RESPONSE"});
  });
  if (!response.ok) {
    const error = new Error(body.error?.message ?? "OpenAI request failed.");
    error.code = "OPENAI_REQUEST_FAILED";
    throw error;
  }

  let request;
  try {
    request = JSON.parse(extractOutputText(body));
  } catch (cause) {
    if (cause.code === "OPENAI_REFUSAL" || cause.code === "OPENAI_INCOMPLETE_RESPONSE") throw cause;
    const error = new Error("OpenAI returned invalid PCG JSON.");
    error.code = "OPENAI_INVALID_JSON";
    error.cause = cause;
    throw error;
  }
  return finalizePcgInterpretation(prompt, request).request;
}

export async function sendToCore(request, fetchImplementation = fetch) {
  const response = await fetchImplementation(coreEndpoint, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request)
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message ?? `Standalone CoreHost returned ${response.status}.`);
    error.code = payload.code ?? "CORE_REQUEST_FAILED";
    throw error;
  }
  if (!payload.world || payload.world.formatVersion !== 1) {
    const error = new Error("Standalone CoreHost returned an unsupported GeneratedWorld document.");
    error.code = "INVALID_GENERATED_WORLD";
    throw error;
  }
  return payload;
}

export async function readCoreHealth(fetchImplementation = fetch) {
  const endpoint = new URL("/health", coreEndpoint);
  const response = await fetchImplementation(endpoint);
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message ?? `Standalone CoreHost returned ${response.status}.`);
    error.code = payload.code ?? "CORE_HOST_UNAVAILABLE";
    throw error;
  }
  return payload;
}

export async function sendToUnity(request, fetchImplementation = fetch) {
  let response;
  try {
    response = await fetchImplementation(unityEndpoint, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(request)
    });
  } catch (cause) {
    const error = new Error("Unity Bridge is unavailable. Open the Unity project and enter Play Mode.");
    error.code = "UNITY_BRIDGE_UNAVAILABLE";
    error.cause = cause;
    throw error;
  }

  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message ?? `Unity Bridge returned ${response.status}.`);
    error.code = payload.code ?? "UNITY_REQUEST_FAILED";
    error.upstreamStatus = response.status;
    throw error;
  }
  return { status: response.status, payload };
}

export async function sendUnitySnapshotCommand(command, fetchImplementation = fetch) {
  const endpoint = new URL(command === "save" ? "/pcg/save" : "/pcg/load", unityEndpoint);
  let response;
  try {
    response = await fetchImplementation(endpoint, { method: "POST" });
  } catch (cause) {
    const error = new Error("Unity Bridge is unavailable. Open the Unity project and enter Play Mode.");
    error.code = "UNITY_BRIDGE_UNAVAILABLE";
    error.cause = cause;
    throw error;
  }
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(payload.message ?? `Unity Bridge returned ${response.status}.`);
    error.code = payload.code ?? "UNITY_REQUEST_FAILED";
    throw error;
  }
  return { status: response.status, payload };
}

async function readJsonBody(request) {
  let body = "";
  for await (const chunk of request) {
    body += chunk;
    if (body.length > 65536) throw new Error("Request body is too large.");
  }
  return JSON.parse(body);
}

function sendJson(response, statusCode, body) {
  response.writeHead(statusCode, { "Content-Type": "application/json; charset=utf-8" });
  response.end(JSON.stringify(body));
}

async function sendStatic(response, filePath, contentType) {
  try {
    const contents = await readFile(filePath);
    response.writeHead(200, { "Content-Type": contentType, "Cache-Control": "no-cache" });
    response.end(contents);
  } catch {
    response.writeHead(404).end();
  }
}

async function sendPreferredStatic(response, preferredPath, fallbackPath, contentType) {
  try {
    const contents = await readFile(preferredPath);
    response.writeHead(200, { "Content-Type": contentType, "Cache-Control": "no-cache" });
    response.end(contents);
  } catch (error) {
    if (error.code !== "ENOENT") throw error;
    await sendStatic(response, fallbackPath, contentType);
  }
}

function safeStaticPath(root, prefix, requestUrl, extension) {
  const relative = decodeURIComponent(requestUrl.slice(prefix.length));
  if (!relative || relative.includes("\\") || relative.split("/").includes("..") || !relative.toLowerCase().endsWith(extension)) return null;
  const resolved = path.resolve(root, relative);
  return resolved.startsWith(`${path.resolve(root)}${path.sep}`) ? resolved : null;
}

export function createWebServer({ requestPcg = requestPcgFromOpenAi, sendRequestToCore = sendToCore, sendRequestToUnity = sendToUnity, sendCommandToUnity = sendUnitySnapshotCommand, readHealth = readCoreHealth } = {}) {
  return createServer(async (request, response) => {
    try {
      const pathname = new URL(request.url ?? "/", "http://127.0.0.1").pathname;
      if (request.method === "GET" && pathname === "/") return sendStatic(response, path.join(publicDirectory, "index.html"), "text/html; charset=utf-8");
      if (request.method === "GET" && (pathname === "/unity" || pathname === "/unity/")) return sendStatic(response, path.join(unityPublicDirectory, "index.html"), "text/html; charset=utf-8");
      if (request.method === "GET" && pathname === "/unity/app.js") return sendStatic(response, path.join(unityPublicDirectory, "app.js"), "text/javascript; charset=utf-8");
      if (request.method === "GET" && pathname === "/unity/styles.css") return sendStatic(response, path.join(unityPublicDirectory, "styles.css"), "text/css; charset=utf-8");
      if (request.method === "GET" && pathname === "/app.js") return sendStatic(response, path.join(publicDirectory, "app.js"), "text/javascript; charset=utf-8");
      if (request.method === "GET" && pathname === "/renderer.js") return sendStatic(response, path.join(publicDirectory, "renderer.js"), "text/javascript; charset=utf-8");
      if (request.method === "GET" && pathname === "/world-codec.mjs") return sendStatic(response, path.join(publicDirectory, "world-codec.mjs"), "text/javascript; charset=utf-8");
      if (request.method === "GET" && pathname === "/styles.css") return sendStatic(response, path.join(publicDirectory, "styles.css"), "text/css; charset=utf-8");
      if (request.method === "GET" && pathname === "/asset-manifest.json") return sendPreferredStatic(response, localAssetManifest, defaultAssetManifest, "application/json; charset=utf-8");
      if (request.method === "GET" && pathname === "/vendor/three.module.js") return sendStatic(response, path.join(threeDirectory, "build/three.module.js"), "text/javascript; charset=utf-8");
      if (request.method === "GET" && pathname === "/vendor/three.core.js") return sendStatic(response, path.join(threeDirectory, "build/three.core.js"), "text/javascript; charset=utf-8");
      if (request.method === "GET" && pathname === "/vendor/OrbitControls.js") return sendStatic(response, path.join(threeDirectory, "examples/jsm/controls/OrbitControls.js"), "text/javascript; charset=utf-8");
      if (request.method === "GET" && pathname.startsWith("/vendor/addons/")) {
        const filePath = safeStaticPath(path.join(threeDirectory, "examples/jsm"), "/vendor/addons/", pathname, ".js");
        return filePath ? sendStatic(response, filePath, "text/javascript; charset=utf-8") : sendJson(response, 404, { ok: false, code: "NOT_FOUND" });
      }
      if (request.method === "GET" && pathname.startsWith("/assets/models/")) {
        const filePath = safeStaticPath(modelDirectory, "/assets/models/", pathname, ".glb");
        return filePath ? sendStatic(response, filePath, "model/gltf-binary") : sendJson(response, 404, { ok: false, code: "NOT_FOUND" });
      }
      if (request.method === "GET" && pathname === "/shared/pcg-request.mjs") return sendStatic(response, path.join(sharedDirectory, "pcg-request.mjs"), "text/javascript; charset=utf-8");
      if (request.method === "GET" && pathname === "/api/health") return sendJson(response, 200, await readHealth());
      if (request.method === "POST" && (pathname === "/api/unity/save" || pathname === "/api/unity/load")) {
        const command = pathname.endsWith("/save") ? "save" : "load";
        const unityResponse = await sendCommandToUnity(command);
        return sendJson(response, 202, { ok: true, unityStatus: unityResponse.status, unity: unityResponse.payload });
      }
      const generationPaths = ["/api/generate", "/api/generate-direct", "/api/render-unity", "/api/unity/generate", "/api/unity/generate-direct"];
      if (request.method !== "POST" || !generationPaths.includes(pathname)) return sendJson(response, 404, { ok: false, code: "NOT_FOUND" });

      let body;
      try { body=await readJsonBody(request); }
      catch { return sendJson(response,400,{ok:false,code:"INVALID_JSON_BODY",message:"A valid JSON request body is required (maximum 65536 characters)."}); }
      if(!body||typeof body!=="object"||Array.isArray(body))return sendJson(response,400,{ok:false,code:"INVALID_JSON_BODY",message:"A JSON object is required."});
      let pcgRequest, interpretation;
      if (pathname === "/api/generate-direct" || pathname === "/api/render-unity" || pathname === "/api/unity/generate-direct") {
        pcgRequest = upgradePcgRequest(body.request);
        const validationError = validatePcgRequest(pcgRequest);
        if (validationError) return sendJson(response, 400, { ok: false, code: "INVALID_PCG_REQUEST", message: validationError });
      } else {
        const prompt = body.prompt;
        if (typeof prompt !== "string" || prompt.trim().length === 0 || prompt.length > 4000) return sendJson(response, 400, { ok: false, code: "INVALID_PROMPT", message: "prompt must be non-empty and at most 4000 characters." });
        ({request:pcgRequest,interpretation}=finalizePcgInterpretation(prompt.trim(),await requestPcg(prompt.trim())));
      }
      if (pathname === "/api/render-unity") {
        const unityResponse = await sendRequestToUnity(pcgRequest);
        return sendJson(response, 200, { ok: true, request: pcgRequest, unityStatus: unityResponse.status, unity: unityResponse.payload });
      }
      if (pathname.startsWith("/api/unity/")) {
        const unityResponse = await sendRequestToUnity(pcgRequest);
        return sendJson(response, 202, { ok: true, request: pcgRequest, interpretation, unityStatus: unityResponse.status, unity: unityResponse.payload });
      }
      const coreResponse = await sendRequestToCore(pcgRequest);
      return sendJson(response, 200, { ok: true, request: pcgRequest, interpretation, world: coreResponse.world });
    } catch (error) {
      const status = error.code==="INVALID_PROMPT_CONSTRAINT" ? 400
        : error.code==="OPENAI_REFUSAL" ? 422
        : ["OPENAI_API_KEY_MISSING", "CORE_HOST_UNAVAILABLE", "UNITY_BRIDGE_UNAVAILABLE"].includes(error.code) ? 503 : 502;
      return sendJson(response, status, { ok: false, code: error.code ?? "WEB_PIPELINE_ERROR", message: error.message });
    }
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const port = Number(process.env.WEB_PORT ?? 3000);
  createWebServer().listen(port, "127.0.0.1", () => console.log(`LLM2PCG Web UI listening on http://127.0.0.1:${port}`));
}
