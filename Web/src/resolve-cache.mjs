// Bounded process-scoped idempotency: never evict and silently recharge a retry.
export function createResolveCache(limit=256) {
  const entries=new Map();
  return {run(id,fingerprint,work) {
    if(typeof id!=="string"||!/^[-a-zA-Z0-9_]{16,80}$/.test(id))throw Object.assign(new Error("requestId must contain 16..80 URL-safe characters."),{code:"INVALID_REQUEST_ID",status:400});
    const prior=entries.get(id);
    if(prior) {
      if(prior.fingerprint!==fingerprint)throw Object.assign(new Error("This requestId belongs to different input."),{code:"REQUEST_ID_CONFLICT",status:409});
      return prior.promise.then(value=>structuredClone(value));
    }
    if(entries.size>=limit)throw Object.assign(new Error("Resolution cache is full. Save resolved JSON before restarting this server."),{code:"RESOLVE_CACHE_FULL",status:503});
    const promise=Promise.resolve().then(work);
    // Keep failures too: a timed-out API request may already have been billed.
    entries.set(id,{fingerprint,promise});
    return promise.then(value=>structuredClone(value));
  }};
}
