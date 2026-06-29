using AdaptadorHuella;
using DPUruNet;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Net.WebSockets;

namespace AdaptadorHuella1;

internal class MatchHandler : IFingerprintHandler
{
    private readonly Func<dynamic?> _getHuellaCliente;
    private readonly ApiClient _apiClient;
    private readonly Action<dynamic?> _setHuellaCliente;
    private readonly Action<dynamic?> _setCurrentClient;

    public MatchHandler(Func<dynamic?> getHuellaCliente, ApiClient apiClient, Action<dynamic?> setHuellaCliente, Action<dynamic?> setCurrentClient)
    {
        _getHuellaCliente = getHuellaCliente;
        _apiClient = apiClient;
        _setHuellaCliente = setHuellaCliente;
        _setCurrentClient = setCurrentClient;
    }

    public async Task HandleAsync(CaptureResult result, string calidad, byte[] imagen, WebSocket socket)
    {
        try
        {
            var fid = result.Data;

            var response = new ResponseBody()
            {
                modo = Modo.MATCH.ToString(),
                match = false,
                Calidad = calidad,
                Lectura = fid.Bytes,
                Imagen = imagen,
                numero = 0
            };

            var huellaCliente = _getHuellaCliente();
            if (huellaCliente != null)
            {
                try
                {
                    byte[] baHuella = huellaCliente;

                    DataResult<Fmd> HuellaBD =
                        Importer.ImportFmd(
                            baHuella,
                            Constants.Formats.Fmd.DP_REGISTRATION,
                            Constants.Formats.Fmd.DP_REGISTRATION
                        );

                    DataResult<Fmd> Actual =
                        FeatureExtraction.CreateFmdFromFid(
                            fid,
                            Constants.Formats.Fmd.ISO
                        );

                    CompareResult resultado =
                        Comparison.Compare(
                            Actual.Data,
                            0,
                            HuellaBD.Data,
                            0
                        );

                    response.Score = resultado.Score;
                    response.match = resultado.Score < 500;
                    await socket.sendBytesAsync(ActionClient.ON_CAPTURED_MATCH, response);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error comparando huellas en match");
                }
            }
            else
            {
                response.mensaje = "El cliente no tiene huella para match";
                await socket.sendBytesAsync(ActionClient.ON_CAPTURED_MATCH, response);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error en MatchHandler.HandleAsync");
        }
    }

    public async Task HandleMessageAsync(string type, JObject payload, WebSocket socket)
    {
        switch (type)
        {
            case "current_client":
                _setCurrentClient(payload);
                var bp = payload["BP"]?.ToString();
                Log.Information("Cliente actual establecido en /match: {BP}", bp);
                if (!string.IsNullOrEmpty(bp))
                {
                    Log.Debug("Obteniendo huella del cliente {BP} desde API...", bp);
                    var huella = await _apiClient.GetHuellaBytesAsync(bp);
                    _setHuellaCliente(huella);
                    var d = new
                    {
                        type = "huella_cliente",
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        existenciaHuellas = huella != null,
                        haylector = Adaptador.HayLector()
                    };
                    if (huella != null)
                        Log.Information("Huella obtenida correctamente para cliente {BP}", bp);
                    else
                        Log.Warning("No se pudo obtener huella para cliente {BP}", bp);
                    await socket.sendBytesAsync(ActionClient.SYNC_ESTADO, d);
                }
                break;
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
        }
    }
}
