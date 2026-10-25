using System.Text.Json;
using LLama.Native;

namespace PharmaX.Assistant.Engine;

/// <summary>
/// The engine's way in: requests in, one per line, and a reply out for
/// each, until the input ends.
/// </summary>
/// <remarks>
/// <para>
/// Called by reflection from PharmaX.Assistant.AssistantHost, which has
/// loaded this assembly into a context of its own, over the standard input
/// and output of the second PharmaX process. The requests are JSON:
/// </para>
/// <code>
/// {"op":"load","model":"...gguf","cache":"...","threads":4,"instructions":"..."}
/// {"op":"ask","turn":"...","grammar":"root ::= ...","maxTokens":160}
/// {"op":"chat","model":"...gguf","threads":4,"prompt":"...","maxTokens":400}
/// </code>
/// <para>
/// Each reply is one line starting with <see cref="ReplyPrefix"/>, so that
/// anything else that reaches standard output is told apart from it. A
/// request that fails is answered with "ok": false and the reason; only a
/// failure in native code ends the process, and PharmaX sees that as the
/// output closing.
/// </para>
/// <para>
/// There is no cancellation: PharmaX stops the process instead, which
/// cannot leave anything half-done that matters. The saved reading is
/// written beside its final name and moved into place.
/// </para>
/// </remarks>
public static class EngineEntry
{
    public const string ReplyPrefix = "PXA ";

    public static int Run(TextReader input, TextWriter output, string directory)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);

        // llama.cpp is looked for in this folder's runtimes, not beside
        // PharmaX; and only its warnings and errors are kept, on standard
        // error, where PharmaX keeps the last few to explain a crash.
        NativeLibraryConfig.All
            .WithSearchDirectory(directory)
            .WithLogCallback((level, message) =>
            {
                if (level is LLamaLogLevel.Warning or LLamaLogLevel.Error)
                    Console.Error.Write(message);
            });

        using var model = new LoadedModel();

        while (input.ReadLine() is string line)
        {
            string reply;

            try
            {
                reply = Handle(model, line, text =>
                {
                    output.WriteLine(ReplyPrefix + JsonSerializer.Serialize(new { ok = true, partial = text }));
                    output.Flush();
                });
            }
#pragma warning disable CA1031 // Every failure becomes a reply; see the remarks.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // The whole chain: "type initializer threw" says nothing on
                // its own; the cause underneath it is what support needs.
                var reasons = new List<string>();
                for (var e = ex; e is not null; e = e.InnerException) reasons.Add(e.Message);
                reply = JsonSerializer.Serialize(new { ok = false, error = string.Join(" <- ", reasons) });
            }

            output.WriteLine(ReplyPrefix + reply);
            output.Flush();
        }

        return 0;
    }

    /// <summary>
    /// One request. A chat also writes its answer out as it goes, each
    /// piece a reply of its own with "partial", before the final one.
    /// </summary>
    private static string Handle(LoadedModel model, string line, Action<string> partial)
    {
        using var request = JsonDocument.Parse(line);
        var r = request.RootElement;

        object reply = r.GetProperty("op").GetString() switch
        {
            "load" => model.Load(
                Text(r, "model"), Text(r, "cache"), r.GetProperty("threads").GetInt32(),
                Text(r, "instructions")),

            "ask" => model.Ask(
                Text(r, "turn"), Text(r, "grammar"), r.GetProperty("maxTokens").GetInt32()),

            "chat" => model.Chat(
                Text(r, "model"), r.GetProperty("threads").GetInt32(), Text(r, "prompt"),
                r.GetProperty("maxTokens").GetInt32(), partial),

            var op => throw new InvalidOperationException($"The assistant has no request '{op}'.")
        };

        return JsonSerializer.Serialize(reply);
    }

    private static string Text(JsonElement request, string name) =>
        request.GetProperty(name).GetString()
        ?? throw new InvalidOperationException($"The request has no '{name}'.");
}
