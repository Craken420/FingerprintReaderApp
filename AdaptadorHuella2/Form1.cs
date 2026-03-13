using Newtonsoft.Json.Bson;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Net.Mime.MediaTypeNames;

namespace AdaptadorHuella2
{
    public partial class Form1 : Form
    {
        // El objeto ClientWebSocket es el encargado de la comunicación
        private ClientWebSocket _webSocket = null;
        private CancellationTokenSource _cts = null;


        public Form1()
        {
            string image = System.IO.File.ReadAllText("File.txt");


            InitializeComponent();
            //var arr = Convert.FromBase64String(image);
            //var bmp = Program.CreateBitmap(arr, 400, 500);
            //var stream = System.Drawing.Image.FromStream(new System.IO.MemoryStream(bmp));
            //this.pictureBox1.Image = stream;
        }

        /// <summary>
        /// Inicia la conexión con el servidor WebSocket
        /// </summary>
        private async Task ConnectAsync()
        {
            try
            {
                _webSocket = new ClientWebSocket();
                _cts = new CancellationTokenSource();

                // Cambia esta URL por la de tu servidor (ejemplo: ws://echo.websocket.org)
                Uri serverUri = new Uri("ws://localhost:5000/ws");

                Log("Conectando a " + serverUri.ToString() + "...");
                await _webSocket.ConnectAsync(serverUri, _cts.Token);

                Log("✔️✔️¡Conectado exitosamente!");

                // Iniciamos el ciclo de recepción de mensajes en segundo plano
                _ = ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                Log("Error de conexión: " + ex.Message);
            }
        }

        /// <summary>
        /// Envía una cadena de texto al servidor
        /// </summary>
        private async Task SendMessageAsync(string message)
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cts.Token);
                Log("Enviado: " + message);
            }
        }


        /// <summary>
        /// Ciclo infinito para escuchar mensajes del servidor
        /// </summary>
        /// <summary>
        /// Ciclo para escuchar mensajes del servidor manejando fragmentación y formato BSON
        /// </summary>
        private async Task ReceiveMessagesAsync()
        {
            byte[] buffer = new byte[1024 * 8];

            try
            {
                while (_webSocket.State == WebSocketState.Open)
                {
                    using (var ms = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);

                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cerrando", _cts.Token);
                                Log("Servidor cerró la conexión.");
                                return;
                            }

                            ms.Write(buffer, 0, result.Count);

                        } while (!result.EndOfMessage);

                        ms.Seek(0, SeekOrigin.Begin);

                        // Si el servidor envía BSON, el tipo suele ser Binary
                        if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            ProcessBsonMessage(ms);
                        }
                        else if (result.MessageType == WebSocketMessageType.Text)
                        {
                            using (var reader = new StreamReader(ms, Encoding.UTF8))
                            {
                                string message = await reader.ReadToEndAsync();
                                ProcessTextMessage(message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (_webSocket.State != WebSocketState.Aborted)
                {
                    Log("Conexión perdida: " + ex.Message);
                }
            }
        }

        private void ProcessTextMessage(string json)
        {
            // Lógica para JSON normal (Base64)
            Log("Mensaje de texto recibido: " + json);
        }

        /// <summary>
        /// Procesa datos en formato binario BSON
        /// </summary>
        private void ProcessBsonMessage(MemoryStream ms)
        {
            try
            {
                // Para BSON usamos BsonDataReader de Newtonsoft.Json.Bson
                using (BsonDataReader reader = new BsonDataReader(ms))
                {
                    JsonSerializer serializer = new JsonSerializer();

                    // Deserializamos a un objeto dinámico o una clase específica
                    dynamic data = serializer.Deserialize(reader);

                    if (data != null && data.type == "fingerprint")
                    {
                        Log("🎁 BSON Recibido: Huella detectada.");

                        // En BSON, los campos binarios suelen venir ya como byte[] 
                        // si el servidor los envió correctamente, evitando el paso de Base64
                        byte[] imageBytes = data.Imagen;

                        this.Invoke((MethodInvoker)delegate {
                            if (imageBytes != null)
                            {
                                //var image = Bitmap.FromStream(new MemoryStream(Program.CreateBitmap(imageBytes, 400, 500)));;
                                var image = Bitmap.FromStream(new MemoryStream(imageBytes));
                                this.pictureBox1.Image = image;
                            }
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error al procesar BSON: " + ex.Message);
            }
        }

        /// <summary>
        /// Escribe mensajes en el TextBox de log de forma segura para hilos
        /// </summary>
        private void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine(message);
        }

        // Asegurarse de liberar recursos al cerrar el formulario
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _cts?.Cancel();
            _webSocket?.Dispose();
            base.OnFormClosing(e);
        }

        private async void Form1_Load(object sender, EventArgs e)
        {
            await ConnectAsync();
        }

        async private void button1_Click(object sender, EventArgs e)
        {
            _cts?.Cancel();
            _webSocket?.Dispose();
            await ConnectAsync();
        }
    }
}
