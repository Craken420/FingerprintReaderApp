using System.Net.WebSockets;
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

WebSocket? captureSocket = null;
WebSocket? matchSocket = null;
WebSocket? wsqSocket = null;
List<Reader> lectores = new();
HashSet<string> initializedSerials = new();

dynamic? huellaCliente = null;

var handlers = new Dictionary<string, IFingerprintHandler>
{
    ["capture"] = new CaptureHandler(pre_enroll, () => wsqSocket, () => huellaCliente),
    ["match"] = new MatchHandler(() => huellaCliente),
};

Console.WriteLine("=================================");
Console.WriteLine("Adaptador Huella iniciado");
Console.WriteLine("WebSocket: ws://localhost:5000/capture");
Console.WriteLine("WebSocket: ws://localhost:5000/match");
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
            _ = BroadcastEstadoAsync(new { haylector = hasReader });
            if (hasReader)
            {
                var pingData = new
                {
                    type = "pong",
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    haylector = true
                };
                _ = BroadcastEstadoAsync(pingData);
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
        var imagen = Adaptador.CreateBitmap(raw, fid.Views[0].Width, fid.Views[0].Height);

        if (captureSocket != null && captureSocket.State == WebSocketState.Open)
        {
            await handlers["capture"].HandleAsync(result, calidad, imagen, captureSocket);
        }
        else if (matchSocket != null && matchSocket.State == WebSocketState.Open)
        {
            await handlers["match"].HandleAsync(result, calidad, imagen, matchSocket);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error captura: " + ex.Message);
    }
}

async Task sendCaptureBytesAsync(ActionClient action, object data)
{
    if (captureSocket != null && captureSocket.State == WebSocketState.Open)
        await captureSocket.sendBytesAsync(action, data);
}

async Task sendMatchBytesAsync(ActionClient action, object data)
{
    if (matchSocket != null && matchSocket.State == WebSocketState.Open)
        await matchSocket.sendBytesAsync(action, data);
}

async Task BroadcastEstadoAsync(object data)
{
    if (captureSocket != null && captureSocket.State == WebSocketState.Open)
        await captureSocket.sendBytesAsync(ActionClient.SYNC_ESTADO, data);
    if (matchSocket != null && matchSocket.State == WebSocketState.Open)
        await matchSocket.sendBytesAsync(ActionClient.SYNC_ESTADO, data);
}

app.Map("/capture", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    captureSocket = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("Cliente conectado a /capture");

    var buffer = new byte[4096];

    while (captureSocket.State == WebSocketState.Open)
    {
        try
        {
            var result = await captureSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None
            );

            if (result.MessageType == WebSocketMessageType.Close)
            {
                pre_enroll.Clear();
                Console.WriteLine("Conexi" + '\u00f3' + "n /capture cerrada");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
            {
                JObject json;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine("Mensaje /capture: " + message);
                    json = JObject.Parse(message);
                }
                else
                {
                    using var stream = new MemoryStream(buffer, 0, result.Count);
                    using var bsonReader = new BsonDataReader(stream);
                    json = JObject.Load(bsonReader);
                }

                var type = json["type"]?.ToString();
                switch (type)
                {
                    case "ping":
                        var haylector = Adaptador.HayLector();
                        var data = new
                        {
                            type = "pong",
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            haylector = haylector
                        };
                        await sendCaptureBytesAsync(ActionClient.PONG, data);
                        break;
                }
                {
                    
                }
            }
        }
        catch (Newtonsoft.Json.JsonReaderException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
        }
        catch { }
    }

    pre_enroll.Clear();
    captureSocket = null;
    Console.WriteLine("Cliente /capture desconectado");
});

app.Map("/match", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    matchSocket = await context.WebSockets.AcceptWebSocketAsync();
    Console.WriteLine("Cliente conectado a /match");

    var buffer = new byte[4096];

    while (matchSocket.State == WebSocketState.Open)
    {
        try
        {
            var result = await matchSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer),
                CancellationToken.None
            );

            if (result.MessageType == WebSocketMessageType.Close)
            {
                Console.WriteLine("Conexi" + '\u00f3' + "n /match cerrada");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
            {
                JObject json;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Console.WriteLine("Mensaje /match: " + message);
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

                switch (type)
                {
                    case "huellaCliente":
                        huellaCliente = payload["huella"];
                        System.Diagnostics.Debug.WriteLine("Huella cliente establecida");
                        break;
                    case "ping":
                        var haylector = Adaptador.HayLector();
                        var data = new
                        {
                            type = "pong",
                            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            haylector = haylector
                        };
                        await sendMatchBytesAsync(ActionClient.PONG, data);
                        break;
                }
            }
        }
        catch (Newtonsoft.Json.JsonReaderException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
            System.Diagnostics.Debug.WriteLine(ex.StackTrace);
        }
        catch { }
    }

    huellaCliente = null;
    matchSocket = null;
    Console.WriteLine("Cliente /match desconectado");
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

    if (captureSocket?.State == WebSocketState.Open)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await captureSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", cts.Token);
        }
        catch { }
    }
    captureSocket = null;

    if (matchSocket?.State == WebSocketState.Open)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await matchSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", cts.Token);
        }
        catch { }
    }
    matchSocket = null;

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
