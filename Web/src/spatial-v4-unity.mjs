import {validateRequestV4} from '../../Shared/pcg-request-v4.mjs';

function endpoint(path) {
  const url=new URL(process.env.PCG_V4_UNITY_BASE_URL??'http://127.0.0.1:8089');
  if(url.protocol!=='http:' || url.hostname!=='127.0.0.1' || url.username || url.password || url.pathname!=='/' || url.search || url.hash)
    throw Object.assign(new Error('Unity v4 must use an HTTP 127.0.0.1 base URL.'),{status:503,code:'INVALID_UNITY_V4_ENDPOINT'});
  return new URL(path,url);
}
async function call(path,options,fetchImplementation) {
  let response;
  try { response=await fetchImplementation(endpoint(path),{...options,signal:AbortSignal.timeout(25000)}); }
  catch(error) {
    if(error.code==='INVALID_UNITY_V4_ENDPOINT')throw error;
    if(['TimeoutError','AbortError'].includes(error.name))throw Object.assign(new Error('Unity 응답 시간이 초과됐습니다. 생성 여부는 Unity 창에서 확인하세요. 자동 재시도하지 않습니다.'),{status:504,code:'UNITY_V4_OUTCOME_UNKNOWN'});
    throw Object.assign(new Error('Unity v4 preview 통신에 실패했습니다. Start HTTP Preview 메뉴와 Unity 결과를 확인하세요. 요청 전달 이후 연결이 끊겼다면 결과가 생성됐을 수도 있습니다. 자동 재시도하지 않습니다.'),{status:503,code:'UNITY_V4_UNAVAILABLE'});
  }
  let payload;try{payload=await response.json();}catch{throw Object.assign(new Error('Invalid Unity JSON response'),{status:502,code:'INVALID_UNITY_V4_RESPONSE'});}
  if(!response.ok || !payload.ok)throw Object.assign(new Error(payload.message??'Unity v4 failed'),{status:[400,403,408,413,415,422,429,503,504].includes(response.status)?response.status:502,code:payload.code??'UNITY_V4_FAILED'});
  return payload;
}
export async function readUnityV4Status({fetchImplementation=fetch}={}) {return call('/pcg/v4/status',{method:'GET'},fetchImplementation);}
export async function sendV4ToUnity(request,{fetchImplementation=fetch}={}) {
  const error=validateRequestV4(request);if(error)throw Object.assign(new Error(error),{status:400,code:'INVALID_V4_REQUEST'});
  const result=await call('/pcg/v4/generate',{method:'POST',headers:{'Content-Type':'application/json','X-PCG-V4':'1'},body:JSON.stringify(request)},fetchImplementation);
  if(result.target!=='unity-editor-preview' || result.rendering?.completed!==true || !/^[a-f0-9]{64}$/.test(result.worldHash??'') || !/^[a-f0-9]{64}$/.test(result.semanticHash??''))
    throw Object.assign(new Error('Unity did not confirm completed rendering.'),{status:502,code:'INVALID_UNITY_V4_RECEIPT'});
  return result;
}
