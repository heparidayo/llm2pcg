export function decodeBase64Bytes(value) {
  if (!value) return new Uint8Array();
  const binary = globalThis.atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

export function decodeBase64Float32(value) {
  const bytes = decodeBase64Bytes(value);
  if (bytes.byteLength % 4 !== 0) throw new Error("base64-f32le buffer length must be divisible by four.");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const values = new Float32Array(bytes.byteLength / 4);
  for (let index = 0; index < values.length; index++) values[index] = view.getFloat32(index * 4, true);
  return values;
}

export function decodeGeneratedWorld(world) {
  if (!world || world.formatVersion !== 1) throw new Error("GeneratedWorld formatVersion 1 is required.");
  if (world.gridEncoding !== "base64-u8") throw new Error(`Unsupported grid encoding: ${world.gridEncoding}`);
  if (world.floatEncoding !== "base64-f32le") throw new Error(`Unsupported float encoding: ${world.floatEncoding}`);
  const expected = world.width * world.height;
  const cells = decodeBase64Bytes(world.cells);
  const elevations = decodeBase64Float32(world.elevations);
  const structureHeights = decodeBase64Float32(world.structureHeights);
  if (cells.length !== expected) throw new Error(`Grid length ${cells.length} does not match ${expected}.`);
  if (elevations.length !== 0 && elevations.length !== expected) throw new Error("Elevation buffer length does not match the grid.");
  if (structureHeights.length !== 0 && structureHeights.length !== expected) throw new Error("Structure-height buffer length does not match the grid.");
  return { ...world, cells, elevations, structureHeights };
}

export function summarizeGeneratedWorld(world) {
  return {
    formatVersion: world.formatVersion,
    worldType: world.worldType,
    generatorVersion: world.generatorVersion,
    seed: world.seed,
    size: `${world.width}×${world.height}`,
    worldHash: world.worldHash,
    start: world.start,
    exit: world.exit,
    statistics: world.statistics,
    props: world.props?.length ?? 0,
    rooms: world.rooms?.length ?? 0,
    payload: {
      cells: `${Math.ceil((world.cells?.length ?? 0) * 3 / 4).toLocaleString()} bytes encoded`,
      elevations: world.elevations ? `${Math.ceil(world.elevations.length * 3 / 4).toLocaleString()} bytes encoded` : "none",
      structureHeights: world.structureHeights ? `${Math.ceil(world.structureHeights.length * 3 / 4).toLocaleString()} bytes encoded` : "none"
    }
  };
}
