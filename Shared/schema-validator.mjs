// Deliberately small validator for the checked-in v4 schema vocabulary.
// No coercion/default injection. Semantic/cross-field validation is a separate step.
export function validateSchema(value, schema, path = "$") {
  if (schema.anyOf) return schema.anyOf.some(branch => !validateSchema(value, branch, path)) ? null : path + ": no matching type";
  if (Object.hasOwn(schema, "const") && value !== schema.const) return path + ": invalid constant";
  if (schema.enum && !schema.enum.includes(value)) return path + ": unsupported value";
  if (schema.type === "null") return value === null ? null : path + ": expected null";
  if (schema.type === "object") {
    if (!value || typeof value !== "object" || Array.isArray(value)) return path + ": expected object";
    if(schema.maxProperties!==undefined&&Object.keys(value).length>schema.maxProperties)return path+": too many properties";
    for (const key of schema.required ?? []) if (!Object.hasOwn(value, key)) return path + "." + key + ": required";
    for (const key of Object.keys(value)) {
      const field=Object.hasOwn(schema.properties??{},key)?schema.properties[key]:schema.additionalProperties;
      if (!field || typeof field!=="object") return path + "." + key + ": unknown field";
      const error = validateSchema(value[key], field, path + "." + key);
      if (error) return error;
    }
  } else if (schema.type === "array") {
    if (!Array.isArray(value)) return path + ": expected array";
    if (schema.minItems !== undefined && value.length < schema.minItems) return path + ": too few items";
    if (schema.maxItems !== undefined && value.length > schema.maxItems) return path + ": too many items";
    for (let i=0;i<value.length;i++) {
      const error = validateSchema(value[i], schema.items, path + "[" + i + "]");
      if (error) return error;
    }
  } else if (schema.type === "integer") {
    if (!Number.isSafeInteger(value) || value < schema.minimum || value > schema.maximum) return path + ": integer outside bounds";
  } else if (schema.type === "boolean") {
    if (typeof value !== "boolean") return path + ": expected boolean";
  } else if (schema.type === "string") {
    if (typeof value !== "string" || (schema.maxLength !== undefined && value.length > schema.maxLength) ||
      (schema.pattern && !new RegExp(schema.pattern).test(value))) return path + ": invalid string";
  }
  return null;
}
