using DPUruNet;
using Newtonsoft.Json.Linq;
using System.Net.WebSockets;

namespace AdaptadorHuella1;

internal interface IFingerprintHandler
{
    Task HandleAsync(CaptureResult result, string calidad, byte[] imagen, WebSocket socket);
    Task HandleMessageAsync(string type, JObject payload, WebSocket socket);
}
