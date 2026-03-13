using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AdaptadorHuella1;
using DPUruNet;
using Newtonsoft.Json.Bson;

var pre_enroll = new PreEnroll();

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

WebSocket? currentSocket = null;
WebSocket? wsqSocket = null;
List<Reader> lectores = new();

string huellaCliente = "";
Modo modo = Modo.CAPTURA;

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

        //await sendBytesAsync(raw);
        //return;

        var imagen = AdaptadorHuella.CreateBitmap(raw, fid.Views[0].Width, fid.Views[0].Height);

        //System.IO.File.WriteAllText("File.txt", lectura);
        bool match = false;

        var response = new ResponseBody()
        {

            modo = modo.ToString(),
            match = match,
            Calidad = calidad,
            Lectura = fid.Bytes,
            Imagen = imagen,
            numero = 0
        };

        if (modo == Modo.MATCH && !string.IsNullOrEmpty(huellaCliente))
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
        else if (modo == Modo.CAPTURA)
        {
            try
            {
                //VerificarHuellListNegr(result);
                DataResult<Fmd> resultconvert = FeatureExtraction.CreateFmdFromFid(result.Data, Constants.Formats.Fmd.DP_PRE_REGISTRATION);
                pre_enroll.Add(resultconvert.Data);
                response.numero = pre_enroll.Count;
                Console.WriteLine(" Es " + pre_enroll.Count);

                if (pre_enroll.Count == 4)
                {
                    DataResult<Fmd> result_enroll = Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.DP_REGISTRATION, pre_enroll);
                    //if(result_enroll.Data != null)
                    //{
                    //    byte[] x = result_enroll.Data.Bytes;
                    //    await sendBytesAsync(x);

                    //}
                }
                
                
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        

        await sendBytesAsync( response );
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error captura: " + ex.Message);
    }
}

async Task sendBytesAsync(dynamic data )
{
    if (currentSocket != null && currentSocket.State == WebSocketState.Open)
    {
        MemoryStream ms = new MemoryStream();
        using (BsonDataWriter writer = new BsonDataWriter(ms))
        {
            var serializer = new Newtonsoft.Json.JsonSerializer();
            serializer.Serialize(writer, data);
        }
        byte[] bsonBytes = ms.ToArray();
        await currentSocket.SendAsync(
            new ArraySegment<byte>(bsonBytes),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None
        );

        Console.WriteLine("Datos enviados al frontend");
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

        try
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
            var json = JsonDocument.Parse(message);

            var type = json.RootElement.GetProperty("type").GetString();

            if (type == "capture")
            {
                modo = Modo.CAPTURA;
                Console.WriteLine("Modo CAPTURA activado");
            }

            if (type == "match")
            {
                modo = Modo.MATCH;
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
app.Map("/wsq", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    wsqSocket = await context.WebSockets.AcceptWebSocketAsync();

    Console.WriteLine("Frontend conectado");
    var buffer = new byte[4096];
    while (wsqSocket.State == WebSocketState.Open)
    {
        var result = await wsqSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer),
            CancellationToken.None
        );
        if (result.MessageType == WebSocketMessageType.Close)
        {
            break;
        }
    };
    Console.WriteLine("Wsq desconectado");
});
app.Run("http://localhost:5000");