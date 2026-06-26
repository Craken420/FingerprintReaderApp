using AdaptadorHuella;
using DPUruNet;
using Serilog;
using System.Net.WebSockets;

namespace AdaptadorHuella1;

internal class MatchHandler : IFingerprintHandler
{
    private readonly Func<dynamic?> _getHuellaCliente;

    public MatchHandler(Func<dynamic?> getHuellaCliente)
    {
        _getHuellaCliente = getHuellaCliente;
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
}
