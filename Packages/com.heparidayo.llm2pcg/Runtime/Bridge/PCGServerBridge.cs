using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Llm2Pcg.Contract;
using UnityEngine;

namespace Llm2Pcg.Bridge
{
    /// <summary>
    /// Editor/local HTTP ingress. HTTP work stays on a background thread; subscribers run from Update.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PCGServerBridge : MonoBehaviour
    {
        private const string GeneratePath = "/pcg/generate";
        private const string SavePath = "/pcg/save";
        private const string LoadPath = "/pcg/load";
        private const int MaximumRequestBytes = 262144;

        [SerializeField, Min(1)] private int port = 8088;
        [SerializeField] private bool startOnAwake;
        [SerializeField, Min(1)] private int maximumQueuedRequests = 16;
        [SerializeField] private string[] allowedBrowserOrigins =
        {
            "http://127.0.0.1:3000",
            "http://localhost:3000"
        };

        private readonly ConcurrentQueue<PCGRequest> requestQueue = new ConcurrentQueue<PCGRequest>();
        private readonly ConcurrentQueue<BridgeCommand> commandQueue = new ConcurrentQueue<BridgeCommand>();
        private readonly object lifecycleLock = new object();
        private HttpListener listener;
        private Thread listenerThread;
        private volatile bool isRunning;
        private string lastServerError;
        private volatile int lastResponseStatusCode;
        private volatile string lastResponseCode = "WAITING";
        private volatile string lastResponseMessage = "Waiting for a request.";

        public static PCGServerBridge Active { get; private set; }
        public event Action<PCGRequest> RequestReceived;
        public event Action SaveRequested;
        public event Action LoadRequested;
        public int Port => port;
        public bool IsRunning => isRunning;
        public int PendingRequestCount => requestQueue.Count;
        public string LastServerError => lastServerError;
        public int LastResponseStatusCode => lastResponseStatusCode;
        public string LastResponseCode => lastResponseCode;
        public string LastResponseMessage => lastResponseMessage;
        public bool LastResponseIsError => lastResponseStatusCode >= 400;

        /// <summary>Configures the loopback-only bridge before it starts.</summary>
        public void ConfigureForLocalDemo(int listenerPort, params string[] browserOrigins)
        {
            if (isRunning)
                throw new InvalidOperationException("Stop the PCG HTTP bridge before changing its configuration.");
            if (listenerPort < 1 || listenerPort > 65535)
                throw new ArgumentOutOfRangeException(nameof(listenerPort), "Port must be between 1 and 65535.");

            port = listenerPort;
            if (browserOrigins != null && browserOrigins.Length > 0)
                allowedBrowserOrigins = browserOrigins;
        }

        private void Awake()
        {
            if (Active != null && Active != this)
            {
                Debug.LogError("Only one PCGServerBridge may run at a time. Disabling duplicate.", this);
                enabled = false;
                return;
            }

            Active = this;
        }

        private void Start()
        {
#if UNITY_EDITOR
            // Keep the main-thread dispatcher alive while a browser or MCP client has focus.
            Application.runInBackground = true;
#endif
            if (startOnAwake)
                StartServer();
        }

        private void Update()
        {
            while (requestQueue.TryDequeue(out PCGRequest request))
            {
                try
                {
                    RequestReceived?.Invoke(request);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
            while (commandQueue.TryDequeue(out BridgeCommand command))
            {
                if (command == BridgeCommand.Save) SaveRequested?.Invoke();
                else LoadRequested?.Invoke();
            }
        }

        private void OnDisable()
        {
            StopServer();
        }

        private void OnDestroy()
        {
            StopServer();
            if (Active == this)
                Active = null;
        }

        private void OnApplicationQuit()
        {
            StopServer();
        }

        public bool StartServer()
        {
            lock (lifecycleLock)
            {
                if (isRunning)
                    return true;

                try
                {
                    HttpListener newListener = new HttpListener();
                    newListener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    newListener.Prefixes.Add("http://localhost:" + port + "/");
                    newListener.Start();

                    listener = newListener;
                    isRunning = true;
                    lastServerError = null;
                    listenerThread = new Thread(ListenLoop)
                    {
                        IsBackground = true,
                        Name = "PCG HTTP Bridge"
                    };
                    listenerThread.Start();
                    Debug.Log("PCG HTTP bridge listening on port " + port + ".", this);
                    return true;
                }
                catch (Exception exception)
                {
                    lastServerError = exception.Message;
                    isRunning = false;
                    listener?.Close();
                    listener = null;
                    Debug.LogError("PCG HTTP bridge could not start on port " + port + ": " + exception.Message, this);
                    return false;
                }
            }
        }

        public void StopServer()
        {
            Thread threadToJoin;
            lock (lifecycleLock)
            {
                if (!isRunning && listener == null)
                    return;

                isRunning = false;
                listener?.Close();
                listener = null;
                threadToJoin = listenerThread;
                listenerThread = null;
            }

            if (threadToJoin != null && threadToJoin.IsAlive && Thread.CurrentThread != threadToJoin)
                threadToJoin.Join(2000);
        }

        private void ListenLoop()
        {
            while (isRunning)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    HandleContext(context);
                }
                catch (HttpListenerException) when (!isRunning)
                {
                    return;
                }
                catch (ObjectDisposedException) when (!isRunning)
                {
                    return;
                }
                catch (Exception exception)
                {
                    lastServerError = exception.Message;
                }
            }
        }

        private void HandleContext(HttpListenerContext context)
        {
            try
            {
                string origin = context.Request.Headers["Origin"];
                if (!IsOriginAllowed(origin, allowedBrowserOrigins))
                {
                    WriteTrackedResponse(context.Response, 403, PCGResponse.Failed("ORIGIN_NOT_ALLOWED", "Browser origin is not allowed by the local bridge."));
                    return;
                }

                AddCorsHeaders(context.Response, origin);
                if (context.Request.HttpMethod == "OPTIONS")
                {
                    WriteTrackedResponse(context.Response, 204, null);
                    return;
                }

                if (context.Request.HttpMethod != "POST")
                {
                    WriteTrackedResponse(context.Response, 404, PCGResponse.Failed("NOT_FOUND", "Use POST /pcg/generate, /pcg/save, or /pcg/load."));
                    return;
                }

                string path = context.Request.Url.AbsolutePath;
                if (path == SavePath || path == LoadPath)
                {
                    commandQueue.Enqueue(path == SavePath ? BridgeCommand.Save : BridgeCommand.Load);
                    WriteTrackedResponse(context.Response, 202, PCGResponse.AcceptedCommand(path == SavePath ? "SAVE_QUEUED" : "LOAD_QUEUED", path == SavePath ? "Snapshot save queued." : "Snapshot load queued."));
                    return;
                }
                if (path != GeneratePath)
                {
                    WriteTrackedResponse(context.Response, 404, PCGResponse.Failed("NOT_FOUND", "Use POST /pcg/generate, /pcg/save, or /pcg/load."));
                    return;
                }

                if (context.Request.ContentLength64 > MaximumRequestBytes)
                {
                    WriteTrackedResponse(context.Response, 413, PCGResponse.Failed("REQUEST_TOO_LARGE", "Request body exceeds 256 KiB."));
                    return;
                }

                string body;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                    body = reader.ReadToEnd();

                if (string.IsNullOrWhiteSpace(body))
                {
                    WriteTrackedResponse(context.Response, 400, PCGResponse.Failed("MALFORMED_JSON", "Request body must contain JSON."));
                    return;
                }

                PCGRequest request;
                try
                {
                    request = JsonUtility.FromJson<PCGRequest>(body);
                }
                catch (ArgumentException)
                {
                    WriteTrackedResponse(context.Response, 400, PCGResponse.Failed("MALFORMED_JSON", "Request body is not valid JSON."));
                    return;
                }

                PCGValidationResult validation = PCGRequestValidator.Validate(request);
                if (!validation.IsValid)
                {
                    WriteTrackedResponse(context.Response, 400, PCGResponse.Failed(validation.Code, validation.Message));
                    return;
                }

                if (requestQueue.Count >= maximumQueuedRequests)
                {
                    WriteTrackedResponse(context.Response, 429, PCGResponse.Failed("REQUEST_QUEUE_FULL", "Too many generation requests are waiting. Try again shortly."));
                    return;
                }

                requestQueue.Enqueue(request);
                WriteTrackedResponse(context.Response, 202, PCGResponse.Accepted(request));
            }
            catch (Exception exception)
            {
                try
                {
                    WriteTrackedResponse(context.Response, 500, PCGResponse.Failed("BRIDGE_ERROR", exception.Message));
                }
                catch (Exception)
                {
                    // The remote client may have disconnected before the response could be written.
                }
            }
        }

        public static bool IsOriginAllowed(string origin, string[] allowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return true;
            if (allowedOrigins == null)
                return false;

            string normalizedOrigin = origin.Trim().TrimEnd('/');
            for (int index = 0; index < allowedOrigins.Length; index++)
            {
                string allowed = allowedOrigins[index];
                if (!string.IsNullOrWhiteSpace(allowed) &&
                    string.Equals(normalizedOrigin, allowed.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void AddCorsHeaders(HttpListenerResponse response, string origin)
        {
            if (!string.IsNullOrWhiteSpace(origin))
            {
                response.Headers["Access-Control-Allow-Origin"] = origin.Trim().TrimEnd('/');
                response.Headers["Vary"] = "Origin";
            }
            response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        }

        private void WriteTrackedResponse(HttpListenerResponse response, int statusCode, PCGResponse payload)
        {
            lastResponseStatusCode = statusCode;
            lastResponseCode = payload == null ? "NO_CONTENT" : payload.code;
            lastResponseMessage = payload == null ? "Preflight request accepted." : payload.message;
            WriteResponse(response, statusCode, payload);
        }

        private static void WriteResponse(HttpListenerResponse response, int statusCode, PCGResponse payload)
        {
            response.StatusCode = statusCode;
            if (payload == null)
            {
                response.Close();
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            response.ContentType = "application/json; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.Length;
            using (Stream output = response.OutputStream)
                output.Write(bytes, 0, bytes.Length);
        }

        private enum BridgeCommand : byte
        {
            Save,
            Load
        }
    }
}
