using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MessageFlowExplorer.Analyzer;

namespace MessageFlowExplorer.Cli;

class Program
{
    static void Main(string[] args)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("========================================");
        Console.WriteLine("       Message Flow Explorer CLI        ");
        Console.WriteLine("========================================");
        Console.ResetColor();

        string inputDir = Directory.GetCurrentDirectory();
        string outputFile = Path.Combine(Directory.GetCurrentDirectory(), "message-flow.json");
        bool serve = false;

        // Simple parsing de argumentos
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--input" || args[i] == "-i")
            {
                if (i + 1 < args.Length)
                {
                    inputDir = args[i + 1];
                    i++;
                }
            }
            else if (args[i] == "--output" || args[i] == "-o")
            {
                if (i + 1 < args.Length)
                {
                    outputFile = args[i + 1];
                    i++;
                }
            }
            else if (args[i] == "--serve" || args[i] == "-s")
            {
                serve = true;
            }
            else if (args[i] == "--help" || args[i] == "-h")
            {
                ShowUsage();
                return;
            }
        }

        if (!Directory.Exists(inputDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: El directorio de entrada '{inputDir}' no existe.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Escaneando código en: {inputDir}");
        
        try
        {
            var scanner = new SolutionScanner();
            // Progreso en vivo: se imprime cada mensaje en cuanto ocurre para que
            // en monorepos grandes se vea avanzar y no parezca bloqueado.
            scanner.OnProgress = msg =>
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"  {msg}");
                Console.ResetColor();
            };
            var report = scanner.ScanDirectory(inputDir);

            Console.WriteLine($"\nEscaneo finalizado con éxito:");
            Console.WriteLine($"  - Mensajes únicos encontrados: {report.Messages.Count}");
            Console.WriteLine($"  - Publicadores (Producers): {report.Producers.Count}");
            Console.WriteLine($"  - Consumidores (Consumers): {report.Consumers.Count}");
            Console.WriteLine($"  - Sagas: {report.Sagas.Count}");
            Console.WriteLine($"  - Actividades (Activities): {report.Activities.Count}");
            Console.WriteLine($"  - Routing Slips: {report.RoutingSlips.Count}");

            foreach (var slip in report.RoutingSlips)
            {
                var pasos = string.Join(" → ", slip.Itinerary.Select(s => s.Name));
                Console.WriteLine($"\n  Routing Slip @ {slip.Location} [{slip.Project}]");
                Console.WriteLine($"    Itinerario: {(string.IsNullOrEmpty(pasos) ? "(vacío)" : pasos)}");
                Console.WriteLine($"    Eventos suscritos: {(slip.SubscribedEvents.Count > 0 ? string.Join(", ", slip.SubscribedEvents) : "ninguno")}");

                if (!slip.IsExecuted)
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("    ℹ No se vio 'bus.Execute' en el mismo método: puede ejecutarse en otro sitio (verifica dónde se dispara).");
                    Console.ResetColor();
                }
                if (!slip.SubscribedEvents.Any(e => e.Contains("Faulted")))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("    ⚠ Sin suscripción a 'Faulted': los fallos/compensaciones podrían pasar desapercibidos.");
                    Console.ResetColor();
                }
                if (!slip.SubscribedEvents.Any(e => e.Contains("Completed")))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("    ⚠ Sin suscripción a 'Completed': no hay confirmación de que la slip finalice.");
                    Console.ResetColor();
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
            string json = JsonSerializer.Serialize(report, options);

            // Crear directorio de salida si no existe
            var outputDir = Path.GetDirectoryName(outputFile);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            File.WriteAllText(outputFile, json);
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nReporte guardado exitosamente en: {outputFile}");
            Console.ResetColor();

            if (serve)
            {
                var server = new EmbeddedWebServer(json);
                try
                {
                    server.Start();
                    server.OpenBrowser();
                    
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("\nPresiona ENTER para detener el servidor...");
                    Console.ResetColor();
                    Console.ReadLine();
                }
                finally
                {
                    server.Stop();
                    Console.WriteLine("Servidor web detenido.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError durante el análisis: {ex.Message}");
            Console.ResetColor();
        }
    }

    static void ShowUsage()
    {
        Console.WriteLine("Uso:");
        Console.WriteLine("  message-flow-explorer [opciones]");
        Console.WriteLine();
        Console.WriteLine("Opciones:");
        Console.WriteLine("  -i, --input <ruta>    Directorio raíz del proyecto o solución a escanear (por defecto: directorio actual)");
        Console.WriteLine("  -o, --output <ruta>   Ruta del archivo JSON resultante (por defecto: message-flow.json en el directorio actual)");
        Console.WriteLine("  -s, --serve           Inicia el servidor web y abre el visualizador en el navegador");
        Console.WriteLine("  -h, --help            Muestra esta pantalla de ayuda");
        Console.WriteLine();
    }
}
