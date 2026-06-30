using DPUruNet;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Collections;
using SixLabors.ImageSharp.Processing;

namespace AdaptadorHuella
{
    public enum Modo
    {
        CAPTURA,
        MATCH,
    };

    public enum ActionClient
    {
        ON_CAPTURED_MATCH,
        ON_CAPTURED_CAPTURA,
        SYNC_ESTADO,
        PONG
    }
    public struct ToDb
    {
        public bool correcto;
        public bool lol;
        public string mensaje;
        public int score;
    }
    public class Adaptador
    {
        public static bool HayLector()
        {
            foreach (Reader R in ReaderCollection.GetReaders())
            {
                return true;
            }
            return false;
        }
        public static byte[] CreateBitmap(byte[] bytes, int width, int height)
        {
            // Crear una nueva imagen con el formato Rgb24 (24 bits por píxel, RGB)  
            using (Image< Rgb24> image = new Image< Rgb24> (width, height))  
            {
                // Iterar sobre cada píxel de la imagen  
                for (int y = 0; y < height; y++)  
                {
                    for (int x = 0; x < width; x++)  
                    {
                        // Calcular el índice en el arreglo de bytes original (escala de grises)  
                        int originalByteIndex = y * width + x;
                        if (originalByteIndex >= bytes.Length)  
                        {
                            // Manejar el caso donde los datos de entrada son insuficientes  
                            // Esto podría indicar un error en las dimensiones o en el arreglo de bytes  
                            continue;
                        }

                        byte grayValue = bytes[originalByteIndex];

                        // Asignar el valor de gris a los componentes R, G y B  
                        // Esto simula una imagen en escala de grises en formato RGB  
                        image[x, y] = new Rgb24(grayValue, grayValue, grayValue);
                    }
                }

                // Guardar la imagen en un MemoryStream como JPEG  
                using (MemoryStream ms = new MemoryStream())
                {
                    image.SaveAsJpeg(ms);
                    return ms.ToArray();
                }
            }
        }

        public static ToDb ToDB(CaptureResult result)
        { //Captura de huella de cliente nuevo
            var data = new ToDb();
            data.lol = true;

            var feature = FeatureExtraction.CreateFmdFromFid(result.Data as Fid, Constants.Formats.Fmd.ANSI);
            if (feature != null)
            {
                
                CompareResult Result_Finger = Comparison.Compare(feature.Data, 0, feature.Data, 0);

                if (Result_Finger.Score > 500)
                {
                    data.lol = false;
                }
            }

            

            NFIQ.NFIQ_SCORE score = NFIQ.GetScore(result.Data as Fid, NFIQ.NFIQ_ALGORITHM.NFIQ_NIST);
            data.score = (int) score;
            switch (score)
            {
                case NFIQ.NFIQ_SCORE.EXCELLENT:

                    data.correcto = true;
                    data.mensaje = "Huella excelente.";
                    break;
                case NFIQ.NFIQ_SCORE.GOOD:
                    data.correcto = true;
                    data.mensaje = "Huella aceptable.";
                    break;
                default:
                    data.mensaje = "Esta huella no cumple con la calidad requerida, Intente de nuevo";
                    break;
            }
            return data;
        }

        public static bool ExisteHuellaSimilar(CaptureResult result, byte[] HuellaCliente)
        {
            
            DataResult<Fmd> BaseDatos = Importer.ImportFmd(HuellaCliente, Constants.Formats.Fmd.DP_REGISTRATION, Constants.Formats.Fmd.DP_REGISTRATION);
            DataResult<Fmd> Actual = FeatureExtraction.CreateFmdFromFid(result.Data as Fid, Constants.Formats.Fmd.ISO);
            CompareResult ResultFinger = Comparison.Compare(Actual.Data, 0, BaseDatos.Data, 0);


            NFIQ.NFIQ_SCORE score = NFIQ.GetScore(result.Data as Fid, NFIQ.NFIQ_ALGORITHM.NFIQ_NIST);

            if (ResultFinger.Score < 2147483)
            {
                return true;
            }
            return false;
        }
    }

    internal class ResponseBody
    {

        public string type { get => "fingerprint"; }

        public string modo { get; set; }

        public bool match {get; set;}

        public string Calidad { get; set; }

        public byte[] Lectura { get; set; }

        public byte[] Imagen { get; set; }
        /*
         * en modo capture indica el número d huella capturada
         */
        public int numero { get; set; }
        /*
         * enviar mensaje al cliente
         */
        public string mensaje { get; set; }
        public int Score { get; set; }
        public bool existenciaHuellas { get; set; }
    }

    internal class PreEnroll : System.Collections.Generic.IList<Fmd>
    {
        Fmd[] pre_enroll = new Fmd[4];
        int index;
        int n = 0;
        public Fmd this[int index] { get => pre_enroll[index]; set => ((IList)pre_enroll)[index] = value; }

        public int Count => n;

        public bool IsSynchronized => pre_enroll.IsSynchronized;

        public object SyncRoot => pre_enroll.SyncRoot;

        public bool IsFixedSize => pre_enroll.IsFixedSize;

        public bool IsReadOnly => pre_enroll.IsReadOnly;

        public int Add(Fmd value)
        {
            if (value == null) throw new ArgumentNullException("value");
            pre_enroll[index] = (Fmd)value;
            n = index + 1;
            index = (index + 1) % pre_enroll.Length;
            return index;
            
        
        }

        public void Clear()
        {
            ((IList)pre_enroll).Clear();
            index = 0;
        }

        public bool Contains(Fmd value)
        {
            return ((IList)pre_enroll).Contains(value);
        }

        public void CopyTo(Fmd[] array, int index)
        {
            pre_enroll.CopyTo(array, index);
        }

        public IEnumerator GetEnumerator()
        {
            return pre_enroll.GetEnumerator();
        }

        public int IndexOf(Fmd value)
        {
            return ((IList)pre_enroll).IndexOf(value);
        }

        public void Insert(int index, Fmd value)
        {
            throw new NotImplementedException();
        }

        public void Remove(Fmd value)
        {
            throw new NotImplementedException();
        }

        public void RemoveAt(int index)
        {
            throw new NotImplementedException();
        }

        void ICollection<Fmd>.Add(Fmd item)
        {
            ((ICollection<Fmd>)pre_enroll).Add(item);
        }

        IEnumerator<Fmd> IEnumerable<Fmd>.GetEnumerator()
        {
            return ((IEnumerable<Fmd>)pre_enroll).GetEnumerator();
        }

        bool ICollection<Fmd>.Remove(Fmd item)
        {
            return ((ICollection<Fmd>)pre_enroll).Remove(item);
        }
    }
}
