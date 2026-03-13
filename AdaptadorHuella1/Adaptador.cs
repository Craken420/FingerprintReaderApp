using DPUruNet;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using System.Collections;
using SixLabors.ImageSharp.Processing;

namespace AdaptadorHuella1
{
    public enum Modo
    {
        CAPTURA,
        MATCH,
    };

    public class AdaptadorHuella
    {
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

    }

    internal class ResponseBody
    {

        public string type { get => "fingerprint"; }

        public string modo { get; set; }

        public bool match {get; set;}

        public string Calidad { get; set; }

        public byte[] Lectura { get; set; }

        public byte[] Imagen { get; set; }

        public int numero { get; set; }
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
