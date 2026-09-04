using System;

namespace Llm2Pcg.Contract
{
    [Serializable]
    public sealed class PCGResponse
    {
        public bool ok;
        public string code;
        public string message;
        public int seed;
        public string worldHash;

        public static PCGResponse Accepted(PCGRequest request)
        {
            return new PCGResponse { ok = true, code = "ACCEPTED", message = "Request queued for generation.", seed = request.seed };
        }

        public static PCGResponse Failed(string code, string message)
        {
            return new PCGResponse { ok = false, code = code, message = message };
        }

        public static PCGResponse AcceptedCommand(string code, string message)
        {
            return new PCGResponse { ok = true, code = code, message = message };
        }
    }
}
