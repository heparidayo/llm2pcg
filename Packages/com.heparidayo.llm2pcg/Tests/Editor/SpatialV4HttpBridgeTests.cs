using System;
using System.Collections;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Llm2Pcg.Bridge;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Llm2Pcg.Tests.EditMode
{
    public sealed class SpatialV4HttpBridgeTests
    {
        [UnityTest] public IEnumerator BusyJobCompletesOnlyWhenPumpedAndDisposeCancelsQueuedWork()
        {
            var reserve=new TcpListener(IPAddress.Loopback,0);reserve.Start();int port=((IPEndPoint)reserve.LocalEndpoint).Port;reserve.Stop();
            int generated=0;var bridge=new SpatialV4HttpBridge(r=>{generated++;return "{\"ok\":true}";});
            var client=new HttpClient {Timeout=TimeSpan.FromSeconds(5)};
            string raw=JsonUtility.ToJson(SpatialV4RenderingTests.Request());
            HttpRequestMessage Request(){var r=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:"+port+"/pcg/v4/generate");r.Headers.Add("X-PCG-V4","1");r.Content=new StringContent(raw,Encoding.UTF8,"application/json");return r;}
            try
            {
                bridge.Start(port);
                var first=client.SendAsync(Request());
                // Give ingress a bounded opportunity to enqueue, without pumping the Unity callback.
                double until=UnityEditor.EditorApplication.timeSinceStartup+1;
                while(UnityEditor.EditorApplication.timeSinceStartup<until)yield return null;
                Assert.That(first.IsCompleted,Is.False);Assert.That(generated,Is.Zero);
                var second=client.SendAsync(Request());while(!second.IsCompleted)yield return null;
                Assert.That(second.Result.StatusCode,Is.EqualTo(HttpStatusCode.TooManyRequests));
                bridge.Pump();while(!first.IsCompleted)yield return null;
                Assert.That(first.Result.StatusCode,Is.EqualTo(HttpStatusCode.OK));Assert.That(generated,Is.EqualTo(1));
                var third=client.SendAsync(Request());until=UnityEditor.EditorApplication.timeSinceStartup+1;
                while(UnityEditor.EditorApplication.timeSinceStartup<until)yield return null;
                bridge.Dispose();bridge.Pump();
                Assert.That(generated,Is.EqualTo(1),"Closing must never later execute queued work");
                // Listener shutdown can close the socket instead of delivering its cancellation response.
                while(!third.IsCompleted)yield return null;
            }
            finally {bridge.Dispose();client.Dispose();}
        }
    }
}
