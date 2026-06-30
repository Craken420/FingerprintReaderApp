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
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code
    )
    .CreateLogger();

var pre_enroll = new PreEnroll();

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseWebSockets();

WebSocket? captureSocket = null;
WebSocket? matchSocket = null;
WebSocket? wsqSocket = null;
List<Reader> lectores = new();
HashSet<string> initializedSerials = new();

var handlers = new Dictionary<string, IFingerprintHandler>
{
    ["capture"] = new CaptureHandler(pre_enroll, () => wsqSocket, new ApiClient()),
    ["match"] = new MatchHandler(new ApiClient()),
};

Log.Information("=================================");
Log.Information("Adaptador Huella iniciado");
Log.Information("WebSocket: ws://localhost:5000/capture (conectate aqui para capturar y hacer el eroll)");
Log.Information("WebSocket: ws://localhost:5000/match (conectate aqui si queres validar un huella)");
Log.Information("WebSocket: ws://localhost:5000/wsq (conectate aquí para recibir el wsq)");
Log.Information("=================================");

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
            catch (Exception ex)
            {
                Log.Warning(ex, "Error en ciclo de sondeo de lectores");
            }
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

                Log.Information("Lector listo: {ReaderName} (SN: {SerialNumber})", reader.Description.Name, sn);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error inicializando lector {ReaderName}", reader.Description?.Name ?? "unknown");
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
                Log.Warning("Lector desconectado: {ReaderName}", r.Description?.Name ?? "unknown");
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
        Log.Error(ex, "Error sondeando lectores");
    }
}

async void Reader_OnCaptured(CaptureResult result)
{
    try
    {
        if (result.Data == null)
            return;

        bool hasCapture = captureSocket != null && captureSocket.State == WebSocketState.Open;
        bool hasMatch = matchSocket != null && matchSocket.State == WebSocketState.Open;
        if (!hasCapture && !hasMatch)
            return;

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

        Log.Debug("Huella capturada (calidad: {Calidad})", calidad);

        var raw = fid.Views[0].RawImage;
        var imagen = Adaptador.CreateBitmap(raw, fid.Views[0].Width, fid.Views[0].Height);

        if (hasCapture)
        {
            await handlers["capture"].HandleAsync(result, calidad, imagen, captureSocket!);
        }
        if (hasMatch)
        {
            await handlers["match"].HandleAsync(result, calidad, imagen, matchSocket!);
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error procesando captura de huella");
    }
}

async Task BroadcastEstadoAsync(object data)
{
    if (captureSocket != null && captureSocket.State == WebSocketState.Open)
        await captureSocket.sendBytesAsync(ActionClient.SYNC_ESTADO, data);
    if (matchSocket != null && matchSocket.State == WebSocketState.Open)
        await matchSocket.sendBytesAsync(ActionClient.SYNC_ESTADO, data);
}

static (string? type, JObject? payload) ParseMessage(WebSocketReceiveResult result, byte[] buffer)
{
    JObject json;

    if (result.MessageType == WebSocketMessageType.Text)
    {
        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
        Log.Debug("Mensaje recibido: {Mensaje}", message);
        json = JObject.Parse(message);
    }
    else
    {
        using var stream = new MemoryStream(buffer, 0, result.Count);
        using var bsonReader = new BsonDataReader(stream);
        json = JObject.Load(bsonReader);
    }

    return (json["type"]?.ToString(), (JObject?)json["payload"]);
}

app.Map("/capture", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    captureSocket = await context.WebSockets.AcceptWebSocketAsync();
    pre_enroll.Clear();
    handlers["capture"].Reset();
    Log.Information("Cliente conectado a /capture");

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
                Log.Information("Cliente /capture desconectado (cierre limpio)");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
            {
                var (type, payload) = ParseMessage(result, buffer);
                if (type != null)
                    await handlers["capture"].HandleMessageAsync(type, payload!, captureSocket);
            }
        }
        catch (Newtonsoft.Json.JsonReaderException ex)
        {
            Log.Error(ex, "Error de parseo JSON en /capture");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error en conexi" + '\u00f3' + "n /capture");
        }
    }

    pre_enroll.Clear();
    captureSocket = null;
    Log.Information("Cliente /capture desconectado");
});

app.Map("/match", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    matchSocket = await context.WebSockets.AcceptWebSocketAsync();
    handlers["match"].Reset();
    Log.Information("Cliente conectado a /match");

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
                Log.Information("Cliente /match desconectado (cierre limpio)");
                break;
            }

            if (result.MessageType == WebSocketMessageType.Text || result.MessageType == WebSocketMessageType.Binary)
            {
                var (type, payload) = ParseMessage(result, buffer);
                if (type != null)
                    await handlers["match"].HandleMessageAsync(type, payload!, matchSocket);
            }
        }
        catch (Newtonsoft.Json.JsonReaderException ex)
        {
            Log.Error(ex, "Error de parseo JSON en /match");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error en conexi" + '\u00f3' + "n /match");
        }
    }

    matchSocket = null;
    Log.Information("Cliente /match desconectado");
});

app.Map("/wsq", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    wsqSocket = await context.WebSockets.AcceptWebSocketAsync();

    Log.Information("Cliente conectado a /wsq");
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
            Log.Error(ex, "Error en conexi" + '\u00f3' + "n /wsq");
        }
    };
    Log.Information("Cliente /wsq desconectado");
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
    Log.Information("Limpiando recursos...");

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

    Log.Information("Recursos liberados.");
    Log.CloseAndFlush();
}

try
{
    app.Run("http://localhost:5000");
}
finally
{
    Log.CloseAndFlush();
}
