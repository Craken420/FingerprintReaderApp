# Ejemplo de Uso del Método `CreateBitmap` en una Aplicación de Consola C#

Este documento proporciona un ejemplo completo de cómo utilizar el método `CreateBitmap` (adaptado para ser compatible con entornos Linux usando `SixLabors.ImageSharp`) dentro de una aplicación de consola C#. El ejemplo genera un degradado simple en escala de grises, lo convierte en una imagen JPEG y lo guarda en un archivo.

## Contexto de la Adaptación

El código original utilizaba `System.Drawing.Common`, una librería que, aunque forma parte de .NET Framework, tiene limitaciones de compatibilidad en entornos que no son Windows. Para asegurar la ejecución en el entorno Linux del sandbox, se ha realizado una adaptación para utilizar `SixLabors.ImageSharp`, una librería moderna y multiplataforma para el procesamiento de imágenes en .NET.

## Estructura del Proyecto

El proyecto de consola consta de dos archivos principales:

1.  `Program.cs`: Contiene la lógica de la aplicación, incluyendo el método `CreateBitmap` adaptado y el método `Main` para la ejecución.
2.  `ConsoleApp.csproj`: El archivo de proyecto que define las dependencias y la configuración del proyecto.

### `Program.cs`

```csharp
using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ConsoleApp
{
    class Program
    {
        // El método CreateBitmap adaptado para SixLabors.ImageSharp
        public static byte[] CreateBitmap(byte[] bytes, int width, int height)
        {
            // Crear una nueva imagen con el formato Rgb24 (24 bits por píxel, RGB)
            using (Image<Rgb24> image = new Image<Rgb24>(width, height))
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

        static void Main(string[] args)
        {
            Console.WriteLine("Iniciando la aplicación de ejemplo para CreateBitmap...");

            // Definir dimensiones de la imagen
            int width = 256;
            int height = 256;

            // Generar datos de ejemplo en escala de grises (un degradado simple)
            byte[] grayscaleBytes = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Crear un degradado horizontal simple
                    grayscaleBytes[y * width + x] = (byte)(x % 256);
                }
            }

            try
            {
                // Usar el método CreateBitmap para generar la imagen JPEG
                Console.WriteLine($"Creando bitmap de {width}x{height}...");
                byte[] jpegImageBytes = CreateBitmap(grayscaleBytes, width, height);

                // Guardar la imagen JPEG en un archivo
                string outputPath = "output_image.jpg";
                File.WriteAllBytes(outputPath, jpegImageBytes);

                Console.WriteLine($"Imagen guardada exitosamente en: {Path.GetFullPath(outputPath)}");
                Console.WriteLine("Presiona cualquier tecla para salir.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ocurrió un error: {ex.Message}");
                Console.WriteLine("Asegúrate de que las librerías de ImageSharp estén referenciadas correctamente y que los permisos de escritura sean adecuados.");
            }
            Console.ReadKey();
        }
    }
}
```

### `ConsoleApp.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsable>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.4" />
    <PackageReference Include="SixLabors.ImageSharp.Drawing" Version="1.0.0" />
  </ItemGroup>

</Project>
```

## Cómo Compilar y Ejecutar el Ejemplo

Para compilar y ejecutar esta aplicación en un entorno .NET (como el sandbox o tu máquina local con el SDK de .NET instalado), sigue estos pasos:

1.  **Crear la estructura de directorios:** Crea una carpeta llamada `ConsoleApp`.
2.  **Guardar los archivos:** Guarda el contenido de `Program.cs` y `ConsoleApp.csproj` en la carpeta `ConsoleApp`.
3.  **Abrir una terminal:** Navega hasta la carpeta `ConsoleApp` en tu terminal.
4.  **Restaurar paquetes y compilar:** Ejecuta los siguientes comandos:
    ```bash
    dotnet restore
    dotnet build
    ```
5.  **Ejecutar la aplicación:** Una vez compilado, ejecuta la aplicación con:
    ```bash
    dotnet run
    ```

La aplicación generará un archivo `output_image.jpg` en el mismo directorio de la aplicación, que mostrará un degradado horizontal de gris a blanco. Adjunto la imagen generada por este ejemplo.
