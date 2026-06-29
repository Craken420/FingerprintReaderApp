using AdaptadorHuella;
using DPUruNet;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using SHM.BuroDigital.FolderNewClient;
using Serilog;
using System.Net.WebSockets;

namespace AdaptadorHuella1;

internal class CaptureHandler : IFingerprintHandler
{
    private readonly PreEnroll _preEnroll;
    private readonly Func<WebSocket?> _getWsqSocket;
    private readonly Func<dynamic?> _getHuellaCliente;
    private readonly Action<dynamic?> _setCurrentClient;

    public CaptureHandler(PreEnroll preEnroll, Func<WebSocket?> getWsqSocket, Func<dynamic?> getHuellaCliente, Action<dynamic?> setCurrentClient)
    {
        _preEnroll = preEnroll;
        _getWsqSocket = getWsqSocket;
        _getHuellaCliente = getHuellaCliente;
        _setCurrentClient = setCurrentClient;
    }

    public async Task HandleAsync(CaptureResult result, string calidad, byte[] imagen, WebSocket socket)
    {
        try
        {
            var fid = result.Data;

            var otro = new ToDb()
            {
                lol = true,
                correcto = true,
                mensaje = "Huella excelente"
            };

            var response = new ResponseBody()
            {
                modo = Modo.CAPTURA.ToString(),
                match = false,
                Calidad = calidad,
                Lectura = fid.Bytes,
                Imagen = imagen,
                numero = 0,
                mensaje = otro.mensaje
            };

            if (otro.correcto && otro.lol)
            {
                var resultconvert = FeatureExtraction.CreateFmdFromFid(
                    result.Data, Constants.Formats.Fmd.DP_PRE_REGISTRATION
                );
                _preEnroll.Add(resultconvert.Data);
                response.numero = _preEnroll.Count;
                Log.Debug("Huella capturada #{Count}/4 en captura", _preEnroll.Count);

                bool encontrado = false;
                var huellaCliente = _getHuellaCliente();
                if (huellaCliente != null)
                {
                    encontrado = Adaptador.ExisteHuellaSimilar(result, (byte[])huellaCliente);
                    if (encontrado)
                        response.mensaje = "La huella coincide con la que se registro anteriormente";
                }
                response.match = encontrado;
                await socket.sendBytesAsync(ActionClient.ON_CAPTURED_CAPTURA, response);

                if (!encontrado && _preEnroll.Count == 4)
                {
                    await FinalizeEnrollment(result);
                }
            }
            else
            {
                await socket.sendBytesAsync(ActionClient.ON_CAPTURED_CAPTURA, response);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error en CaptureHandler.HandleAsync");
        }
    }

    private async Task FinalizeEnrollment(CaptureResult result)
    {
        var result_enroll = Enrollment.CreateEnrollmentFmd(
            Constants.Formats.Fmd.DP_REGISTRATION, _preEnroll
        );

        var wsqSocket = _getWsqSocket();

        if (result_enroll.ResultCode == Constants.ResultCode.DP_SUCCESS)
        {
            byte[] x = result_enroll.Data.Bytes;
            var CrearWsq = WSQ.CompressNIST(result.Data, 94, 24000);
            var q = new
            {
                wsq = CrearWsq,
                huella = x,
                status = result_enroll.ResultCode.ToString()
            };
            await SendWsqBytesAsync(q, wsqSocket);
            _preEnroll.Clear();
        }
        else
        {
            var q = new
            {
                status = result_enroll.ResultCode.ToString()
            };
            await SendWsqBytesAsync(q, wsqSocket);
            _preEnroll.Clear();
        }
    }

    public async Task HandleMessageAsync(string type, JObject payload, WebSocket socket)
    {
        switch (type)
        {
            case "ping":
                var haylector = Adaptador.HayLector();
                var data = new
                {
                    type = "pong",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    haylector
                };
                await socket.sendBytesAsync(ActionClient.PONG, data);
                break;
            case "current_client":
                _setCurrentClient(payload);
                Log.Information("Cliente actual establecido en /capture: {BP}", payload["BP"]);
                break;
        }
    }

    private async Task SendWsqBytesAsync(dynamic data, WebSocket? wsqSocket)
    {
        if (wsqSocket != null && wsqSocket.State == WebSocketState.Open)
        {
            MemoryStream ms = new MemoryStream();
            using (BsonDataWriter writer = new BsonDataWriter(ms))
            {
                var serializer = new Newtonsoft.Json.JsonSerializer();
                serializer.Serialize(writer, data);
            }
            byte[] bsonBytes = ms.ToArray();
            await wsqSocket.SendAsync(
                new ArraySegment<byte>(bsonBytes),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
            Log.Debug("Datos WSQ enviados al frontend");
        }
    }
}
