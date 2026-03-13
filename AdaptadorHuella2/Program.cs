using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using Fleck; // Importar la librería

namespace AdaptadorHuella2
{
    internal static class Program
    {

        
        /// <summary>
        /// Punto de entrada principal para la aplicación.
        /// </summary>
        [STAThread]
        static void Main()
        {
            
                
            

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        public static byte[] CreateBitmap(byte[] bytes, int width, int height)
        {
            byte[] rgbBytes = new byte[bytes.Length * 3];

            for (int i = 0; i <= bytes.Length - 1; i++)
            {
                rgbBytes[(i * 3)] = bytes[i];
                rgbBytes[(i * 3) + 1] = bytes[i];
                rgbBytes[(i * 3) + 2] = bytes[i];
            }
            //return rgbBytes;
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);

            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            for (int i = 0; i <= bmp.Height - 1; i++)
            {
                var p = new IntPtr(data.Scan0.ToInt64() + data.Stride * i);
                //System.Runtime.InteropServices.Marshal.Copy(rgbBytes, i * bmp.Width * 3, p, bmp.Width * 3);
                System.Runtime.InteropServices.Marshal.Copy(bytes, i * bmp.Width, p, bmp.Width);
            }

            bmp.UnlockBits(data);

            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Jpeg);
                byte[] byteImage = ms.ToArray();
                return byteImage;
            }
            
        }
    }
}
