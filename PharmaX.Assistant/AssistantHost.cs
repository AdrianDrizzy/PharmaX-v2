using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

namespace PharmaX.Assistant;

/// <summary>
/// The second PharmaX process that runs the reports assistant's model.
/// </summary>
/// <remarks>
/// <para>
/// PharmaX starts itself again with <see cref="Switch"/>, and that copy
/// does nothing but this: it loads the engine from the "assistant" folder
/// beside PharmaX into an assembly load context of its own and hands it
/// standard input and output. It opens no window and no database.
/// </para>
/// <para>
/// A process of its own because llama.cpp is native code holding two
/// gigabytes: if it fails, or is stopped half-way through a question, the
/// till carries on. A load context of its own because the engine brings
/// newer Microsoft.Extensions and System.Text.Json than PharmaX runs on,
/// and here they cannot meet. And a copy of PharmaX rather than a helper
/// program because PharmaX already carries the .NET runtime; a helper
/// would have needed another seventy megabytes of it.
/// </para>
/// </remarks>
public static class AssistantHost
{
    public const string Switch = "--assistant-engine";

    private const string EngineFile = "PharmaX.Assistant.Engine.dll";
    private const string EntryType = "PharmaX.Assistant.Engine.EngineEntry";

    /// <summary>The folder the build copies the engine into, beside PharmaX.</summary>
    public static string EngineDirectory => Path.Combine(AppContext.BaseDirectory, "assistant");

    public static bool IsInstalled => File.Exists(Path.Combine(EngineDirectory, EngineFile));

    public static bool IsRequested(IReadOnlyList<string> args) =>
        args is { Count: > 0 } && args[0] == Switch;

    /// <summary>
    /// Runs the engine over this process's standard input and output,
    /// until PharmaX closes the input or stops the process.
    /// </summary>
    public static int Run()
    {
        // A crash in native code ends this process quietly. Windows would
        // otherwise put up "PharmaX has stopped working" over a till that
        // has not stopped at all.
        if (OperatingSystem.IsWindows())
            _ = NativeMethods.SetErrorMode(NativeMethods.NoCrashDialogs);

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var input = new StreamReader(Console.OpenStandardInput(), utf8);
        using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };

        return RunEngine(input, output);
    }

    /// <summary>
    /// Loads the engine into its own context and runs it over the given
    /// reader and writer. Public so that a test can run it without a
    /// second process.
    /// </summary>
    public static int RunEngine(TextReader input, TextWriter output, string? directory = null)
    {
        directory ??= EngineDirectory;
        var path = Path.Combine(directory, EngineFile);

        var context = new EngineLoadContext(path);
        var entry = context.LoadFromAssemblyPath(path).GetType(EntryType, throwOnError: true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("The assistant's engine has no way in.");

        try
        {
            return (int)entry.Invoke(null, new object[] { input, output, directory })!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    /// <summary>
    /// The engine's assemblies from its own folder, as its deps.json lists
    /// them; anything it does not carry comes from the runtime as usual.
    /// </summary>
    private sealed class EngineLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public EngineLoadContext(string engine) : base("PharmaX assistant engine") =>
            _resolver = new AssemblyDependencyResolver(engine);

        protected override Assembly? Load(AssemblyName assemblyName) =>
            _resolver.ResolveAssemblyToPath(assemblyName) is string path
                ? LoadFromAssemblyPath(path)
                : null;

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName) =>
            _resolver.ResolveUnmanagedDllToPath(unmanagedDllName) is string path
                ? LoadUnmanagedDllFromPath(path)
                : IntPtr.Zero;
    }

    private static class NativeMethods
    {
        /// SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX
        public const uint NoCrashDialogs = 0x0001 | 0x0002;

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern uint SetErrorMode(uint mode);
    }
}
