using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DPUruNet;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

WebSocket? currentSocket = null;

List<Reader> lectores = new();

string huellaCliente = "";
string modo = "capture";

Console.WriteLine("=================================");
Console.WriteLine("Adaptador Huella iniciado");
Console.WriteLine("WebSocket: ws://localhost:5000/ws");
Console.WriteLine("=================================");

InitReader();

void InitReader()
{
    try
    {
        ReaderCollection readerCollection = ReaderCollection.GetReaders();

        if (readerCollection.Count == 0)
        {
            Console.WriteLine("No se encontró lector.");
            return;
        }

        foreach (Reader reader in readerCollection)
        {
            try
            {
                lectores.Add(reader);

                reader.Open(Constants.CapturePriority.DP_PRIORITY_EXCLUSIVE);

                reader.CaptureAsync(
                    Constants.Formats.Fid.ANSI,
                    Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                    reader.Capabilities.Resolutions[0]
                );

                reader.On_Captured += Reader_OnCaptured;

                Console.WriteLine("Lector listo: " + reader.Description.Name);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error lector: " + ex.Message);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error inicializando lector: " + ex.Message);
    }
}

async void Reader_OnCaptured(CaptureResult result)
{
    try
    {
        if (result.Data == null)
            return;

        Console.WriteLine("Huella capturada");

        var fid = result.Data;

        int score = Quality.NfiqFid(
            fid,
            0,
            QualityAlgorithm.QUALITY_NFIQ_NIST
        );

        string calidad = score switch
        {
            1 => "EXCELENTE",
            2 => "BUENA",
            _ => "MALA"
        };

        var raw = fid.Views[0].RawImage;

        string imagen = Convert.ToBase64String(raw);

        string lectura = Convert.ToBase64String(fid.Bytes);

        bool match = false;

        if (modo == "match" && !string.IsNullOrEmpty(huellaCliente))
        {
            try
            {
                byte[] baHuella = Convert.FromBase64String(huellaCliente);

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

                match = resultado.Score < 500;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error comparación: " + ex.Message);
            }
        }

        var response = new
        {
            type = "fingerprint",
            modo = modo,
            match = match,
            Calidad = calidad,
            Lectura = lectura,
            Imagen = imagen
        };

        if (currentSocket != null && currentSocket.State == WebSocketState.Open)
        {
            var json = JsonSerializer.Serialize(response);

            var bytes = Encoding.UTF8.GetBytes(json);

            await currentSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None
            );

            Console.WriteLine("Datos enviados al frontend");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error captura: " + ex.Message);
    }
}

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    currentSocket = await context.WebSockets.AcceptWebSocketAsync();

    Console.WriteLine("Frontend conectado");

    var buffer = new byte[4096];

    while (currentSocket.State == WebSocketState.Open)
    {
        var result = await currentSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer),
            CancellationToken.None
        );

        if (result.MessageType == WebSocketMessageType.Close)
        {
            break;
        }

        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

        Console.WriteLine("Mensaje frontend: " + message);

        try
        {
            var json = JsonDocument.Parse(message);

            var type = json.RootElement.GetProperty("type").GetString();

            if (type == "capture")
            {
                modo = "capture";
                Console.WriteLine("Modo CAPTURA activado");
            }

            if (type == "match")
            {
                modo = "match";
                huellaCliente = json.RootElement
                    .GetProperty("huella")
                    .GetString();

                Console.WriteLine("Modo MATCH activado");
            }
        }
        catch
        {
            Console.WriteLine("Mensaje no reconocido");
        }
    }

    Console.WriteLine("Frontend desconectado");
});

app.Run("http://localhost:5000");