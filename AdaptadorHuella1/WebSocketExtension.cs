using Newtonsoft.Json.Bson;
using System;
using System.Net.WebSockets;
using AdaptadorHuella;

namespace AdaptadorHuella1
{
    public static class WebSocketExtension
    {
        /*
         * envia datos en formato binario (bson)
         * */
        public static async Task sendBytesAsync(this WebSocket ws, ActionClient action, dynamic payload)
        {
            if (ws != null && ws.State == WebSocketState.Open)
            {
                var p = new
                {
                    type = action,
                    payload = payload
                };
                MemoryStream ms = new MemoryStream();
                using (BsonDataWriter writer = new BsonDataWriter(ms))
                {
                    var serializer = new Newtonsoft.Json.JsonSerializer();

                    serializer.Serialize(writer, p);
                }
                byte[] bsonBytes = ms.ToArray();
                await ws.SendAsync(
                    new ArraySegment<byte>(bsonBytes),
                    WebSocketMessageType.Binary,
                    true,
                    CancellationToken.None
                );

                Console.WriteLine("Datos wsq enviados al frontend");
            }
        }
    }
}
