import assert from "node:assert/strict";
import test from "node:test";
import { decodeBase64Bytes, decodeBase64Float32, decodeGeneratedWorld, summarizeGeneratedWorld } from "../public/world-codec.mjs";

test("decodes compact u8 and little-endian float buffers", () => {
  assert.deepEqual([...decodeBase64Bytes(Buffer.from([0, 1, 2, 255]).toString("base64"))], [0, 1, 2, 255]);
  const source = Buffer.alloc(12);
  source.writeFloatLE(0.25, 0); source.writeFloatLE(3.5, 4); source.writeFloatLE(-1, 8);
  assert.deepEqual([...decodeBase64Float32(source.toString("base64"))], [0.25, 3.5, -1]);
});

test("validates GeneratedWorld v1 buffer lengths and produces a compact summary", () => {
  const floats = Buffer.alloc(16); for (let index = 0; index < 4; index++) floats.writeFloatLE(index * 0.25, index * 4);
  const world = {
    formatVersion: 1, worldType: "Forest", generatorVersion: "forest-biome@1", seed: 234, width: 2, height: 2,
    worldHash: "1234ABCD", gridEncoding: "base64-u8", floatEncoding: "base64-f32le", cellLegend: ["Ground","Water"],
    cells: Buffer.from([0, 1, 0, 0]).toString("base64"), elevations: floats.toString("base64"), structureHeights: floats.toString("base64"),
    start: {x:0,y:0}, exit: {x:1,y:1}, statistics: {Ground:3,Water:1,Props:0}, props: [], rooms: []
  };
  const decoded = decodeGeneratedWorld(world);
  assert.deepEqual([...decoded.cells], [0, 1, 0, 0]);
  assert.deepEqual([...decoded.elevations], [0, 0.25, 0.5, 0.75]);
  assert.equal(summarizeGeneratedWorld(world).worldHash, "1234ABCD");
  assert.throws(() => decodeGeneratedWorld({ ...world, cells: Buffer.from([0]).toString("base64") }), /Grid length/);
});
