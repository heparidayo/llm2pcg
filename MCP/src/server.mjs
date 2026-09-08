import { createInterface } from "node:readline";
import { readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { validateRequestV4, requestV4Schema } from "../../Shared/pcg-request-v4.mjs";
import { upgradePcgRequest, validatePcgRequest } from "../../Shared/pcg-request.mjs";
import { parseStrictJson } from "../../Shared/strict-json.mjs";

const moduleDirectory = path.dirname(fileURLToPath(import.meta.url));
const requestSchema = JSON.parse(await readFile(path.resolve(moduleDirectory, "../../Shared/Schema/pcg-request.schema.json"), "utf8"));
const defaultUnityEndpoint = process.env.UNITY_PCG_ENDPOINT ?? "http://127.0.0.1:8088/pcg/generate";

function errorResponse(id, code, message) {
  return { jsonrpc: "2.0", id, error: { code, message } };
}

function toolError(message) {
  return { content: [{ type: "text", text: message }], isError: true };
}

function toolDefinition(name = "generate_world") {
  return {
    name,
    description: name === "generate_dungeon" ? "Legacy alias: generate a deterministic Dungeon." : "Generate a deterministic Dungeon, City, Forest, Cave, Swamp, Snowfield, or Desert in the running Unity Editor.",
    inputSchema: {
      type: "object",
      additionalProperties: false,
      required: ["request"],
      properties: { request: requestSchema }
    }
  };
}

export function createMcpHandler({ fetchImplementation = fetch, unityEndpoint = defaultUnityEndpoint, v4Endpoint = process.env.PCG_V4_CORE_ENDPOINT ?? "http://127.0.0.1:8090/api/v4/world/generate" } = {}) {
  return async function handleMessage(message) {
    if (!message || message.jsonrpc !== "2.0" || typeof message.method !== "string") return errorResponse(message?.id ?? null, -32600, "Invalid JSON-RPC request.");
    const { id, method, params = {} } = message;
    if (method === "notifications/initialized") return null;
    if (method === "initialize") {
      return { jsonrpc: "2.0", id, result: { protocolVersion: params.protocolVersion ?? "2025-03-26", capabilities: { tools: {} }, serverInfo: { name: "llm2pcg-mcp", version: "0.1.0" } } };
    }
    if (method === "tools/list") return { jsonrpc: "2.0", id, result: { tools: [toolDefinition(), toolDefinition("generate_dungeon"), {
      name:"generate_world_v4",description:"Experimental Forest, Desert, Snowfield and Swamp spatial/semantic generation in .NET CoreHost. This tool returns world data, not a rendered Unity scene. Supply a resolved v4 request with seed; no LLM call is made.",
      inputSchema:{type:"object",additionalProperties:false,required:["request"],properties:{request:requestV4Schema}}
    }] } };
    if (method !== "tools/call") return errorResponse(id, -32601, `Unsupported method: ${method}`);
    if (params.name === "generate_world_v4") {
      if(!params.arguments||typeof params.arguments!=="object"||Array.isArray(params.arguments)||Object.keys(params.arguments).length!==1||!Object.hasOwn(params.arguments,"request"))
        return {jsonrpc:"2.0",id,result:toolError("INVALID_V4_REQUEST: Provide only a resolved request argument.")};
      const request=params.arguments?.request;
      const invalid=validateRequestV4(request);
      if(invalid) return {jsonrpc:"2.0",id,result:toolError("INVALID_V4_REQUEST: "+invalid)};
      try {
        const response=await fetchImplementation(v4Endpoint,{method:"POST",headers:{"Content-Type":"application/json"},body:JSON.stringify(request),signal:AbortSignal.timeout(30000)});
        const payload=await response.json();
        if(!response.ok||!payload.ok)return {jsonrpc:"2.0",id,result:toolError((payload.code??"V4_CORE_FAILED")+": "+(payload.message??"Generation failed"))};
        return {jsonrpc:"2.0",id,result:{content:[{type:"text",text:JSON.stringify(payload)}]}};
      } catch(error) {return {jsonrpc:"2.0",id,result:toolError("V4_CORE_UNAVAILABLE: "+error.message)};}
    }
    if (params.name !== "generate_world" && params.name !== "generate_dungeon") return { jsonrpc: "2.0", id, result: toolError(`Unknown tool: ${params.name}`) };

    const request = upgradePcgRequest(params.arguments?.request);
    const validationError = validatePcgRequest(request);
    if (validationError) return { jsonrpc: "2.0", id, result: toolError(`Invalid PCGRequest: ${validationError}`) };
    try {
      const response = await fetchImplementation(unityEndpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(request)
      });
      const payload = await response.json().catch(() => ({}));
      if (!response.ok) return { jsonrpc: "2.0", id, result: toolError(`Unity rejected the request (${payload.code ?? response.status}): ${payload.message ?? "Unknown error"}`) };
      return { jsonrpc: "2.0", id, result: { content: [{ type: "text", text: JSON.stringify({ request, unity: payload }) }] } };
    } catch (error) {
      return { jsonrpc: "2.0", id, result: toolError(`Unity endpoint is unavailable: ${error.message}`) };
    }
  };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const handleMessage = createMcpHandler();
  const input = createInterface({ input: process.stdin, crlfDelay: Infinity });
  input.on("line", async (line) => {
    let response;
    try {
      response = await handleMessage(parseStrictJson(line));
    } catch (error) {
      response = errorResponse(null, -32700, `Parse error: ${error.message}`);
    }
    if (response !== null) process.stdout.write(`${JSON.stringify(response)}\n`);
  });
}
