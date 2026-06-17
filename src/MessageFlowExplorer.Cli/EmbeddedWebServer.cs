using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MessageFlowExplorer.Cli;

public class EmbeddedWebServer
{
    private readonly string _topologyJson;
    private readonly HttpListener _listener;
    private readonly Dictionary<string, string> _resourcesMap;
    private readonly int _port;
    private bool _isRunning;

    public EmbeddedWebServer(string topologyJson, int port = 5000)
    {
        _topologyJson = topologyJson;
        _port = port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _resourcesMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        InitializeResourcesMap();
    }

    private void InitializeResourcesMap()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();

        foreach (var name in resourceNames)
        {
            // name es por ejemplo: "MessageFlowExplorer.Cli.wwwroot.index.html"
            // o "MessageFlowExplorer.Cli.wwwroot.assets.index-E8aw5Sw3.js"
            int wwwrootIndex = name.IndexOf("wwwroot", StringComparison.OrdinalIgnoreCase);
            if (wwwrootIndex >= 0)
            {
                string relativeKey = name.Substring(wwwrootIndex + "wwwroot".Length).TrimStart('.');
                _resourcesMap[relativeKey] = name;
            }
        }
    }

    public void Start()
    {
        _isRunning = true;
        _listener.Start();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\nServidor web embebido iniciado en: http://localhost:{_port}/");
        Console.ResetColor();

        Task.Run(() => ListenLoop());
    }

    public void OpenBrowser()
    {
        string url = $"http://localhost:{_port}/";
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url.Replace("&", "^&")}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\nNo se pudo abrir el navegador automáticamente: {ex.Message}");
            Console.WriteLine($"Por favor, abre tu navegador manualmente e ingresa a: {url}");
            Console.ResetColor();
        }
    }

    private async Task ListenLoop()
    {
        while (_isRunning)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context));
            }
            catch (HttpListenerException)
            {
                // Ocurre cuando el Listener se detiene
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error en el loop de escucha: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            string rawPath = request.Url?.AbsolutePath ?? "/";
            string path = rawPath;

            // Manejo de la API dinámica
            if (path.Equals("/api/topology", StringComparison.OrdinalIgnoreCase))
            {
                byte[] data = Encoding.UTF8.GetBytes(_topologyJson);
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = data.Length;
                response.OutputStream.Write(data, 0, data.Length);
                response.OutputStream.Close();
                return;
            }

            // Normalización para archivos embebidos
            if (path == "/" || string.IsNullOrEmpty(path))
            {
                path = "/index.html";
            }

            string resourceKey = path.TrimStart('/').Replace('/', '.');
            
            if (_resourcesMap.TryGetValue(resourceKey, out string? resourceName))
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                
                if (stream != null)
                {
                    response.ContentType = GetContentType(path);
                    response.ContentLength64 = stream.Length;
                    stream.CopyTo(response.OutputStream);
                    response.OutputStream.Close();
                    return;
                }
            }

            // Si no se encuentra, retornar 404
            response.StatusCode = (int)HttpStatusCode.NotFound;
            byte[] notFoundData = Encoding.UTF8.GetBytes("404 - Archivo no encontrado");
            response.ContentType = "text/plain; charset=utf-8";
            response.OutputStream.Write(notFoundData, 0, notFoundData.Length);
            response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            try
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errorData = Encoding.UTF8.GetBytes($"500 - Error Interno: {ex.Message}");
                response.ContentType = "text/plain; charset=utf-8";
                response.OutputStream.Write(errorData, 0, errorData.Length);
                response.OutputStream.Close();
            }
            catch
            {
                // Ignorar fallos al cerrar la respuesta fallida
            }
        }
    }

    private string GetContentType(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "application/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".svg" => "image/svg+xml; charset=utf-8",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".json" => "application/json; charset=utf-8",
            ".ico" => "image/x-icon",
            _ => "application/octet-stream"
        };
    }

    public void Stop()
    {
        _isRunning = false;
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // Ignorar fallos al detener
        }
    }
}
