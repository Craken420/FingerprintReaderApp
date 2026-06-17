using System;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AdaptadorHuella;
using DPUruNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Linq;
using SHM.BuroDigital.FolderNewClient;
using AdaptadorHuella1;
using Microsoft.Win32;
using System.Runtime.InteropServices;

var pre_enroll = new PreEnroll();

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

WebSocket? currentSocket = null;
WebSocket? wsqSocket = null;
List<Reader> lectores = new();
HashSet<string> initializedSerials = new();

dynamic? huellaCliente = null;
Modo modo = Modo.CAPTURA;

var messageHandlers = new Dictionary<string, Action<WebSocket, JObject>>
{
    ["huellaCliente"] = (ws, payload) => {
        huellaCliente = payload["huella"];
        System.Diagnostics.Debug.WriteLine("Huella clliente esptablecida");
    },
    
    ["ping"] = async (ws, payload) => {
        var haylector = Adaptador.HayLector();
        var data = new
        {
            type = "pong",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            haylector = haylector
        };
        await sendBytesAsync(ActionClient.PONG, data);
    },
    ["capture"] = (ws, payload) => {
        modo = Modo.CAPTURA;
        Console.WriteLine("Modo CAPTURA activado");
        System.Diagnostics.Debug.WriteLine("Modo CAPTURA activado");
    },
    ["match"] = (ws, payload) => {
        modo = Modo.MATCH;
        huellaCliente = payload["huella"]?.ToString() ?? "";
        Console.WriteLine("Modo MATCH activado");
    }
};

Console.WriteLine("=================================");
Console.WriteLine("Adaptador Huella iniciado");
Console.WriteLine("WebSocket: ws://localhost:5000/ws");
Console.WriteLine("=================================");


InitReader();

void InitReader()
{
    TryInitReaders();
    StartReaderPolling();
}

void StartReaderPolling()
{
    _ = Task.Run(async () =>
    {
        while (true)
        {
            try
            {
                await Task.Delay(3000);
                TryInitReaders();
            }
            catch { }
        }
    });
}

void TryInitReaders()
{
    try
    {
        bool hadReader = lectores.Count > 0;
        ReaderCollection readers = ReaderCollection.GetReaders();
        HashSet<string> currentSerials = new();

        foreach (Reader reader in readers)
        {
            string sn = reader.Description?.SerialNumber ?? "";
            if (sn.Length > 0)
                currentSerials.Add(sn);

            if (sn.Length > 0 && initializedSerials.Contains(sn))
                continue;

            try
            {
                lectores.Add(reader);

                if (sn.Length > 0)
                    initializedSerials.Add(sn);

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

        for (int i = lectores.Count - 1; i >= 0; i--)
        {
            Reader r = lectores[i];
            string sn = r.Description?.SerialNumber ?? "";
            if (sn.Length > 0 && !currentSerials.Contains(sn))
            {
                try { r.Dispose(); } catch { }
                lectores.RemoveAt(i);
                initializedSerials.Remove(sn);
                Console.WriteLine("Lector desconectado: " + (r.Description?.Name ?? "unknown"));
            }
        }

        bool hasReader = lectores.Count > 0;
        if (hadReader != hasReader)
        {
            _ = sendBytesAsync(ActionClient.SYNC_ESTADO, new { haylector = hasReader });
            if (hasReader)
            {
                var pingData = new
                {
                    type = "pong",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    haylector = true
                };
                _ = sendBytesAsync(ActionClient.PONG, pingData);
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error sondeando lectores: " + ex.Message);
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

        var imagen = Adaptador.CreateBitmap(raw, fid.Views[0].Width, fid.Views[0].Height);

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

        if (modo == Modo.MATCH)
        {
            if(huellaCliente != null)
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

                    match = resultado.Score < 500;
                    await sendBytesAsync( ActionClient.ON_CAPTURED_MATCH, response);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error comparación: " + ex.Message);
                }
            } else
            {
                response.mensaje = "El cliente no tiene huela para maatch";

            }
        }
        else if (modo == Modo.CAPTURA)
        {
            try
            {
                //dynamic otro = Adaptador.ToDB(result);
                var otro = new ToDb()
                {
                    lol = true,
                    correcto = true,
                    mensaje = "Huella excelente"
                };
                response.mensaje = otro.mensaje;
                if (otro.correcto && otro.lol)
                {

                    //VerificarHuellListNegr(result);
                    DataResult<Fmd> resultconvert = FeatureExtraction.CreateFmdFromFid(result.Data, Constants.Formats.Fmd.DP_PRE_REGISTRATION);
                    pre_enroll.Add(resultconvert.Data);
                    response.numero = pre_enroll.Count;
                    Console.WriteLine(" Es " + pre_enroll.Count);
                    bool encontrado = false;
                    if (huellaCliente != null)
                    {
                        encontrado = Adaptador.ExisteHuellaSimilar(result, (byte[])huellaCliente);
                        if (encontrado)
                            response.mensaje = "La huella coincide con la que se registro anteriormente";
                    }
                    response.match = encontrado;
                    await sendBytesAsync(ActionClient.ON_CAPTURED_CAPTURA, response);
                    if (encontrado == false && pre_enroll.Count == 4)
                    {
                        DataResult<Fmd> result_enroll = Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.DP_REGISTRATION, pre_enroll);


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

                            await setBytesWsqAsync(q);
                            pre_enroll.Clear();
                        } else
                        {
                            var q = new
                            {
                                status = result_enroll.ResultCode.ToString()
                            };
                            await setBytesWsqAsync(q);
                            pre_enroll.Clear();
                        }
                    }
                }
                else
                {
                    await sendBytesAsync(ActionClient.ON_CAPTURED_CAPTURA, response);
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                System.Diagnostics.Debug.WriteLine(ex.Message);
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
            }
        }

        

        
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error captura: " + ex.Message);
    }
}

async Task setBytesWsqAsync(dynamic data)
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

        Console.WriteLine("Datos wsq enviados al frontend");
    }
}

async Task sendBytesAsync(ActionClient action, dynamic data )
{
    if (currentSocket != null && currentSocket.State == WebSocketState.Open)
    {
        Console.WriteLine("SendByteAsync: ", action);
        MemoryStream ms = new MemoryStream();
        var p = new
        {
            type = action.ToString(),
            payload = data
        };
        using (BsonDataWriter writer = new BsonDataWriter(ms))
        {
            var serializer = new Newtonsoft.Json.JsonSerializer();
            serializer.Serialize(writer, p);
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
                pre_enroll.Clear();
                Console.WriteLine("👉👉Conexión cerrada");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
            {
                System.Diagnostics.Debug.WriteLine("👉MessageType:" + result.MessageType);

                JObject json;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine("Mensaje frontend: " + message);
                    json = JObject.Parse(message);
                }
                else
                {
                    using var stream = new MemoryStream(buffer, 0, result.Count);
                    using var bsonReader = new BsonDataReader(stream);
                    json = JObject.Load(bsonReader);
                }

                var type = json["type"]?.ToString();
                var payload = (JObject)json["payload"]!;
                System.Diagnostics.Debug.WriteLine("🎁🎁TYPE:" + type);
                if (messageHandlers.TryGetValue(type!, out var handler))
                {
                    //manejar el mensaje solicituado
                    handler(currentSocket, payload);
                    
                }
                else
                {
                    Console.WriteLine($"Tipo de mensaje no reconocido: {type}");
                }
            }
        }
        catch(Newtonsoft.Json.JsonReaderException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
        }
        catch(Exception ex)
        {
            Console.WriteLine("Mensaje no reconocido");
        }
    }

    pre_enroll.Clear();
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
        try
        {
            var result = await wsqSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer),
            CancellationToken.None
        );
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    };
    Console.WriteLine("Wsq desconectado");
});
AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
{
    CleanupResources().GetAwaiter().GetResult();
};

Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    CleanupResources().GetAwaiter().GetResult();
    Environment.Exit(0);
};

PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    CleanupResources().GetAwaiter().GetResult();
    Environment.Exit(0);
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    CleanupResources().GetAwaiter().GetResult();
});

async Task CleanupResources()
{
    Console.WriteLine("Limpiando recursos...");

    if (currentSocket?.State == WebSocketState.Open)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await currentSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", cts.Token);
        }
        catch { }
    }
    currentSocket = null;

    if (wsqSocket?.State == WebSocketState.Open)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await wsqSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", cts.Token);
        }
        catch { }
    }
    wsqSocket = null;

    foreach (var reader in lectores)
    {
        try { reader.Dispose(); } catch { }
    }
    lectores.Clear();

    pre_enroll.Clear();

    Console.WriteLine("Recursos liberados.");
}

app.Run("http://localhost:5000");
