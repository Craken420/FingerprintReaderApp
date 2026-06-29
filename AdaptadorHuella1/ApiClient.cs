using RestSharp;
using Newtonsoft.Json.Linq;
using Serilog;

namespace AdaptadorHuella1;

public class ApiClient
{
    private readonly RestClient _client;

    public ApiClient(string baseUrl = "https://biometrico-api.mavi.fun")
    {
        _client = new RestClient(new RestClientOptions(baseUrl)
        {
            Timeout = TimeSpan.FromSeconds(15)
        });
    }

    public async Task<JObject?> GetHuellaClienteAsync(string bp)
    {
        try
        {
            Log.Debug("Consultando A_GET_HuellaCliente para BP: {BP}", bp);

            var request = new RestRequest("A_GET_HuellaCliente");
            request.AddQueryParameter("cliente", bp);

            var response = await _client.GetAsync(request);

            if (!response.IsSuccessful)
            {
                Log.Warning("A_GET_HuellaCliente respondi" + '\u00f3' + " con status {StatusCode}", response.StatusCode);
                return null;
            }

            if (string.IsNullOrEmpty(response.Content))
            {
                Log.Warning("A_GET_HuellaCliente devolvi" + '\u00f3' + " contenido vac" + '\u00ed' + "o");
                return null;
            }

            var json = JObject.Parse(response.Content);
            Log.Debug("A_GET_HuellaCliente respuesta recibida correctamente");
            return json;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error llamando A_GET_HuellaCliente para BP: {BP}", bp);
            return null;
        }
    }

    public async Task<byte[]?> GetHuellaBytesAsync(string bp)
    {
        var json = await GetHuellaClienteAsync(bp);
        if (json == null)
            return null;

        var huellaToken = json["SHM_Features"];
        if (huellaToken == null)
        {
            Log.Warning("A_GET_HuellaCliente no contiene campo 'huella'");
            return null;
        }

        try
        {
            return huellaToken.ToObject<byte[]>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error convirtiendo 'huella' a byte[]");
            return null;
        }
    }
}
