using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Llm2Pcg.Core.V4;

namespace Llm2Pcg.Bridge
{
    /// <summary>Opt-in loopback preview ingress. Call Pump on the Unity main thread; Dispose on reload/close.</summary>
    public sealed class SpatialV4HttpBridge : IDisposable
    {
        public const int DefaultPort=8089;
        public const int MaximumMapSize=256;
        private readonly Func<SpatialRequest,string> generate;
        private readonly HttpListener listener=new HttpListener();
        private readonly SemaphoreSlim readers=new SemaphoreSlim(4,4);
        private volatile bool running;
        private Job pending;
        private sealed class Job
        {
            public SpatialRequest request;
            public int state; // 0 queued, 1 executing, 2 cancelled/finished
            public readonly TaskCompletionSource<Reply> completion=new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private sealed class Reply { public int status;public string body; }
        public bool IsRunning=>running;
        public SpatialV4HttpBridge(Func<SpatialRequest,string> generator) {generate=generator??throw new ArgumentNullException(nameof(generator));}
        public void Start(int port=DefaultPort)
        {
            if(running)return;
            if(port<1 || port>65535)throw new ArgumentOutOfRangeException(nameof(port));
            listener.Prefixes.Add("http://127.0.0.1:"+port+"/");
            listener.Start();running=true;_ = Listen();
        }
        private async Task Listen()
        {
            while(running)
            {
                HttpListenerContext context;
                try {context=await listener.GetContextAsync().ConfigureAwait(false);}
                catch(Exception) when(!running) {return;}
                catch(Exception) {Dispose();return;}
                if(!readers.Wait(0)){await Write(context,Error(429,"V4_UNITY_BUSY"));continue;}
                _ = Handle(context);
            }
        }
        private async Task Handle(HttpListenerContext c)
        {
            try
            {
                if(c.Request.RemoteEndPoint==null || !IPAddress.IsLoopback(c.Request.RemoteEndPoint.Address)) {await Write(c,Error(403,"LOOPBACK_ONLY"));return;}
                string origin=c.Request.Headers["Origin"];
                if(!string.IsNullOrEmpty(origin)) {await Write(c,Error(403,"USE_NODE_PROXY"));return;}
                if(c.Request.HttpMethod=="GET" && c.Request.Url.AbsolutePath=="/pcg/v4/status")
                {await Write(c,new Reply {status=200,body="{\"ok\":true,\"unityPreviewReady\":true,\"maximumMapSize\":256}"});return;}
                if(c.Request.HttpMethod!="POST" || c.Request.Url.AbsolutePath!="/pcg/v4/generate") {await Write(c,Error(404,"NOT_FOUND"));return;}
                if(c.Request.Headers["X-PCG-V4"]!="1" || !(c.Request.ContentType??"").Split(';')[0].Trim().Equals("application/json",StringComparison.OrdinalIgnoreCase))
                {await Write(c,Error(415,"JSON_PROXY_REQUEST_REQUIRED"));return;}
                string body;
                using(var buffer=new MemoryStream())
                {
                    byte[] chunk=new byte[4096];
                    var bodyDeadline=Task.Delay(5000);
                    while(true)
                    {
                        var read=c.Request.InputStream.ReadAsync(chunk,0,chunk.Length);
                        if(await Task.WhenAny(read,bodyDeadline).ConfigureAwait(false)!=read) {c.Request.InputStream.Close();await Write(c,Error(408,"BODY_TIMEOUT"));return;}
                        int n=await read.ConfigureAwait(false);if(n==0)break;
                        if(buffer.Length+n>65536){await Write(c,Error(413,"REQUEST_TOO_LARGE"));return;}
                        buffer.Write(chunk,0,n);
                    }
                    body=new UTF8Encoding(false,true).GetString(buffer.ToArray());
                }
                var request=SpatialRequestJson.Parse(body);
                if(request.mapWidth>MaximumMapSize || request.mapHeight>MaximumMapSize) {await Write(c,Error(422,"V4_PREVIEW_SIZE_LIMIT"));return;}
                var job=new Job {request=request};
                if(Interlocked.CompareExchange(ref pending,job,null)!=null){await Write(c,Error(429,"V4_UNITY_BUSY"));return;}
                if(!running){Interlocked.Exchange(ref pending,null);await Write(c,Error(503,"V4_UNITY_STOPPED"));return;}
                if(await Task.WhenAny(job.completion.Task,Task.Delay(20000)).ConfigureAwait(false)!=job.completion.Task)
                {
                    bool cancelled=Interlocked.CompareExchange(ref job.state,2,0)==0;
                    if(cancelled)Interlocked.CompareExchange(ref pending,null,job);
                    await Write(c,Error(504,cancelled?"V4_UNITY_QUEUE_TIMEOUT":"V4_UNITY_OUTCOME_UNKNOWN"));return;
                }
                await Write(c,await job.completion.Task.ConfigureAwait(false));
            }
            catch(ConstraintFailure e){await Write(c,Error(e.Code=="INVALID_V4_REQUEST"?400:422,e.Code));}
            catch(DecoderFallbackException){await Write(c,Error(400,"INVALID_UTF8"));}
            catch(Exception){await Write(c,Error(500,"V4_UNITY_BRIDGE_FAILED"));}
            finally {readers.Release();}
        }
        public void Pump()
        {
            var job=pending;if(job==null || Interlocked.CompareExchange(ref job.state,1,0)!=0)return;
            try {job.completion.TrySetResult(new Reply {status=200,body=generate(job.request)});}
            catch(ConstraintFailure e){job.completion.TrySetResult(Error(e.Code=="INVALID_V4_REQUEST"?400:422,e.Code));}
            catch(Exception){job.completion.TrySetResult(Error(500,"V4_UNITY_RENDER_FAILED"));}
            finally {Interlocked.Exchange(ref job.state,2);Interlocked.CompareExchange(ref pending,null,job);}
        }
        private static Reply Error(int status,string code)=>new Reply {status=status,body="{\"ok\":false,\"code\":\""+code+"\",\"message\":\""+code+"\"}"};
        private static async Task Write(HttpListenerContext c,Reply r)
        {
            try {byte[] bytes=Encoding.UTF8.GetBytes(r.body);c.Response.StatusCode=r.status;c.Response.KeepAlive=false;c.Response.ContentType="application/json; charset=utf-8";
                c.Response.ContentLength64=bytes.Length;await c.Response.OutputStream.WriteAsync(bytes,0,bytes.Length).ConfigureAwait(false);}
            catch(HttpListenerException){}catch(ObjectDisposedException){}catch(IOException){}
            finally {try {c.Response.Close();}catch(ObjectDisposedException){}catch(HttpListenerException){}}
        }
        public void Dispose()
        {
            running=false;listener.Close();var job=Interlocked.Exchange(ref pending,null);
            if(job!=null){Interlocked.CompareExchange(ref job.state,2,0);job.completion.TrySetResult(Error(503,"V4_UNITY_STOPPED"));}
        }
    }
}
