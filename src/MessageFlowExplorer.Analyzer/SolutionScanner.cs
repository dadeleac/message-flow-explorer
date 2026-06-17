using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using MessageFlowExplorer.Core;

namespace MessageFlowExplorer.Analyzer;

public class SolutionScanner
{
    private static readonly string[] ExcludedDirectories = { "bin", "obj", ".git", ".vs", "node_modules", "tests" };

    // MSBuildLocator debe registrarse una sola vez por proceso y ANTES de
    // que se carguen tipos de MSBuild/Workspaces. Lo hacemos de forma perezosa
    // y tolerante a fallos (si no hay SDK disponible, caemos al modo ad-hoc).
    private static bool _msbuildRegistered;
    private static readonly object _registerLock = new();

    /// <summary>Mensajes de diagnóstico del último escaneo (para mostrarlos en el CLI).</summary>
    public List<string> Diagnostics { get; } = new();

    public TopologyReport ScanDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"El directorio especificado no existe: {directoryPath}");
        }

        Diagnostics.Clear();

        var producers = new List<ProducerInfo>();
        var consumers = new List<ConsumerInfo>();
        var sagas = new List<SagaInfo>();
        var activities = new List<ActivityInfo>();
        var routingSlips = new List<RoutingSlipInfo>();
        var messageTypes = new HashSet<string>();

        // Buscar proyectos C# reales en el árbol. Si los hay, usamos MSBuild para
        // obtener compilaciones con TODAS las referencias resueltas (MassTransit,
        // MediatR, etc.). Eso hace que el SemanticModel resuelva los tipos por su
        // nombre totalmente cualificado y evita falsos "mensajes huérfanos".
        var csprojFiles = FindProjectFiles(directoryPath);

        bool analyzedWithMsBuild = false;
        if (csprojFiles.Count > 0)
        {
            try
            {
                AnalyzeWithMsBuild(csprojFiles, producers, consumers, sagas, activities, routingSlips, messageTypes);
                analyzedWithMsBuild = true;
            }
            catch (Exception ex)
            {
                Diagnostics.Add($"[Aviso] Análisis con MSBuild falló ({ex.Message}). Usando análisis sintáctico de respaldo.");
                producers.Clear();
                consumers.Clear();
                sagas.Clear();
                activities.Clear();
                routingSlips.Clear();
                messageTypes.Clear();
            }
        }

        if (!analyzedWithMsBuild)
        {
            if (csprojFiles.Count == 0)
            {
                Diagnostics.Add("[Info] No se encontraron archivos .csproj. Usando análisis sintáctico de archivos sueltos (sin referencias externas).");
            }
            AnalyzeAdHoc(directoryPath, producers, consumers, sagas, activities, routingSlips, messageTypes);
        }

        return BuildReport(producers, consumers, sagas, activities, routingSlips, messageTypes);
    }

    private List<string> FindProjectFiles(string directoryPath)
    {
        return Directory.EnumerateFiles(directoryPath, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !ExcludedDirectories.Any(dir => file.Split(Path.DirectorySeparatorChar).Contains(dir)))
            .ToList();
    }

    // ---------------------------------------------------------------------
    // Modo MSBuild: carga real de proyectos con referencias resueltas
    // ---------------------------------------------------------------------
    private void AnalyzeWithMsBuild(
        List<string> csprojFiles,
        List<ProducerInfo> producers,
        List<ConsumerInfo> consumers,
        List<SagaInfo> sagas,
        List<ActivityInfo> activities,
        List<RoutingSlipInfo> routingSlips,
        HashSet<string> messageTypes)
    {
        EnsureMsBuildRegistered();

        using var workspace = MSBuildWorkspace.Create();
        // Diagnósticos de carga (proyectos que no restauran, etc.). No abortan
        // el análisis: trabajamos con lo que sí se haya podido cargar.
        workspace.RegisterWorkspaceFailedHandler(e =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
            {
                lock (Diagnostics) { Diagnostics.Add($"[MSBuild] {e.Diagnostic.Message}"); }
            }
        });

        foreach (var csproj in csprojFiles)
        {
            // Un .csproj puede haberse cargado ya como referencia transitiva de otro
            // (p. ej. un proyecto de contratos compartido). Evitamos recargarlo.
            var fullPath = Path.GetFullPath(csproj);
            if (workspace.CurrentSolution.Projects.Any(p =>
                    p.FilePath != null && Path.GetFullPath(p.FilePath).Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                workspace.OpenProjectAsync(csproj).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Diagnostics.Add($"[Aviso] No se pudo cargar el proyecto '{Path.GetFileName(csproj)}': {ex.Message}");
            }
        }

        var projects = workspace.CurrentSolution.Projects
            .Where(p => p.Language == LanguageNames.CSharp)
            .ToList();

        if (projects.Count == 0)
        {
            throw new InvalidOperationException("Ningún proyecto C# pudo cargarse con MSBuild.");
        }

        Diagnostics.Add($"[Info] {projects.Count} proyecto(s) C# cargado(s) con MSBuild (referencias resueltas).");

        foreach (var project in projects)
        {
            var compilation = project.GetCompilationAsync().GetAwaiter().GetResult();
            if (compilation == null) continue;

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (ShouldSkipTree(tree.FilePath)) continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var walker = new MassTransitSyntaxWalker(semanticModel, "", project.Name);
                walker.Visit(tree.GetRoot());
                Collect(walker, producers, consumers, sagas, activities, routingSlips, messageTypes);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Modo ad-hoc: glob de archivos .cs + compilación mínima (respaldo)
    // ---------------------------------------------------------------------
    private void AnalyzeAdHoc(
        string directoryPath,
        List<ProducerInfo> producers,
        List<ConsumerInfo> consumers,
        List<SagaInfo> sagas,
        List<ActivityInfo> activities,
        List<RoutingSlipInfo> routingSlips,
        HashSet<string> messageTypes)
    {
        var csharpFiles = Directory.EnumerateFiles(directoryPath, "*.cs", SearchOption.AllDirectories)
            .Where(file => !ExcludedDirectories.Any(dir => file.Split(Path.DirectorySeparatorChar).Contains(dir)))
            .ToList();

        var syntaxTrees = csharpFiles
            .Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file), path: file))
            .ToList();

        var compilation = CSharpCompilation.Create("MessageFlowAnalysis")
            .AddSyntaxTrees(syntaxTrees)
            .AddReferences(GetBaseReferences());

        foreach (var tree in syntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var walker = new MassTransitSyntaxWalker(semanticModel, directoryPath);
            walker.Visit(tree.GetRoot());
            Collect(walker, producers, consumers, sagas, activities, routingSlips, messageTypes);
        }
    }

    // Referencias base para el modo ad-hoc. Defensivo frente a publicaciones
    // single-file, donde Assembly.Location devuelve "" y CreateFromFile fallaría:
    // si no encontramos los ensamblados en disco, simplemente no se añaden y los
    // tipos del framework quedan sin resolver (el walker usa entonces sus
    // fallbacks sintácticos). Los tipos definidos en el código escaneado sí se
    // resuelven igualmente porque están en los SyntaxTrees.
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "SingleFile", "IL3000",
        Justification = "Acceso a Assembly.Location protegido; degradación elegante si está vacío.")]
    private static MetadataReference[] GetBaseReferences()
    {
        var refs = new List<MetadataReference>();

        void TryAdd(string? path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
            }
            catch { /* ignorar referencias no resolubles */ }
        }

        var coreLib = typeof(object).Assembly.Location;
        TryAdd(coreLib);
        TryAdd(typeof(Console).Assembly.Location);

        var dir = string.IsNullOrEmpty(coreLib) ? null : Path.GetDirectoryName(coreLib);
        if (dir != null)
        {
            TryAdd(Path.Combine(dir, "System.Runtime.dll"));
            TryAdd(Path.Combine(dir, "netstandard.dll"));
            TryAdd(Path.Combine(dir, "System.Collections.dll"));
        }

        return refs.ToArray();
    }

    private static bool ShouldSkipTree(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return false;
        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // Excluir código autogenerado (obj/) y assembly attributes.
        return parts.Contains("obj") || parts.Contains("bin") ||
               filePath.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase) ||
               filePath.EndsWith(".AssemblyAttributes.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static void Collect(
        MassTransitSyntaxWalker walker,
        List<ProducerInfo> producers,
        List<ConsumerInfo> consumers,
        List<SagaInfo> sagas,
        List<ActivityInfo> activities,
        List<RoutingSlipInfo> routingSlips,
        HashSet<string> messageTypes)
    {
        producers.AddRange(walker.Producers);
        consumers.AddRange(walker.Consumers);
        sagas.AddRange(walker.Sagas);
        activities.AddRange(walker.Activities);
        routingSlips.AddRange(walker.RoutingSlips);

        foreach (var prod in walker.Producers) messageTypes.Add(prod.MessageType);
        foreach (var cons in walker.Consumers) messageTypes.Add(cons.MessageType);
        foreach (var saga in walker.Sagas)
        {
            foreach (var ev in saga.ConsumedEvents) messageTypes.Add(ev);
            foreach (var pub in saga.PublishedMessages) messageTypes.Add(pub);
        }
        foreach (var act in walker.Activities)
        {
            messageTypes.Add(act.ArgumentsType);
            if (act.CompensateLogType != null) messageTypes.Add(act.CompensateLogType);
        }
    }

    private static TopologyReport BuildReport(
        List<ProducerInfo> producers,
        List<ConsumerInfo> consumers,
        List<SagaInfo> sagas,
        List<ActivityInfo> activities,
        List<RoutingSlipInfo> routingSlips,
        HashSet<string> messageTypes)
    {
        var messages = messageTypes.Select(type =>
        {
            string category = "Event"; // Por defecto, asumimos Event
            if (type.EndsWith("Command") || type.Contains("Command"))
            {
                category = "Command";
            }
            else if (type.EndsWith("Request") || type.Contains("Request"))
            {
                category = "Request";
            }
            else
            {
                var relatedProducers = producers.Where(p => p.MessageType == type).ToList();
                if (relatedProducers.Any(p => p.CallType == "Request"))
                {
                    category = "Request";
                }
                else if (relatedProducers.Any(p => p.CallType == "Send"))
                {
                    category = "Command";
                }
            }

            return new MessageInfo(type, category);
        }).ToList();

        return new TopologyReport(
            messages,
            producers.Distinct().ToList(),
            consumers.Distinct().ToList(),
            sagas.Distinct().ToList(),
            activities.Distinct().ToList(),
            routingSlips,
            DateTime.UtcNow
        );
    }

    private static void EnsureMsBuildRegistered()
    {
        if (_msbuildRegistered) return;
        lock (_registerLock)
        {
            if (_msbuildRegistered) return;
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
            _msbuildRegistered = true;
        }
    }
}
