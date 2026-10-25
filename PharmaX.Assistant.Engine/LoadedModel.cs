using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace PharmaX.Assistant.Engine;

/// <summary>
/// The model, and its reading of the instructions, kept between questions.
/// </summary>
/// <remarks>
/// Reading the instructions is the slow part - a minute or more on a
/// pharmacy PC - so the model's state after reading them is saved and
/// loaded in a fraction of a second next time. The file is named after a
/// hash of the model and the instructions, so a change to either (a new
/// report, a new member of staff) simply makes a new one.
/// </remarks>
internal sealed class LoadedModel : IDisposable
{
    private const uint ContextTokens = 8192;

    /// <summary>Tokens fed to the model per step.</summary>
    private const int ReadStep = 256;

    /// <summary>Saved readings kept; older ones are deleted.</summary>
    private const int KeptStates = 4;

    private LLamaWeights? _weights;
    private LLamaContext? _llm;
    private LLamaContext? _chat;
    private LLamaContext.State? _afterInstructions;
    private int _instructionTokens;
    private string _modelPath = "";
    private int _threads;

    public object Load(string modelPath, string cacheDirectory, int threads, string instructions)
    {
        var clock = Stopwatch.StartNew();

        if (_weights is null || _modelPath != modelPath || _threads != threads)
        {
            Dispose();
            _modelPath = modelPath;
            _threads = threads;
            _weights = LLamaWeights.LoadFromFile(Parameters());
        }

        // A fresh context for a new set of instructions: a recurrent
        // model's memory cannot be wound back part of the way.
        _afterInstructions?.Dispose();
        _afterInstructions = null;
        _llm?.Dispose();
        _llm = _weights.CreateContext(Parameters());

        var tokens = _llm.Tokenize(instructions, addBos: true, special: true);

        if (tokens.Length > ContextTokens - 512)
            throw new InvalidOperationException(
                "The report list is too long for the assistant to read.");

        var cache = CachePath(cacheDirectory, instructions);
        var reused = TryLoad(cache);

        if (!reused)
        {
            Read(tokens, 0, wantAnswer: false, new LLamaBatch());
            Save(cache);
        }

        _afterInstructions = _llm.GetState();
        _instructionTokens = tokens.Length;

        return new { ok = true, tokens = tokens.Length, reused, ms = clock.ElapsedMilliseconds };
    }

    public object Ask(string turn, string grammar, int maxTokens)
    {
        if (_llm is null || _afterInstructions is null || _weights is null)
            throw new InvalidOperationException("The assistant has not read its instructions yet.");

        var clock = Stopwatch.StartNew();
        _llm.LoadState(_afterInstructions);

        var question = _llm.Tokenize(turn, addBos: false, special: true);
        var batch = new LLamaBatch();
        var position = Read(question, _instructionTokens, wantAnswer: true, batch);

        using var sampler = new DefaultSamplingPipeline
        {
            Temperature = 0f,
            Grammar = new Grammar(grammar, "root")
        };

        var decoder = new StreamingTokenDecoder(_llm);

        for (var i = 0; i < maxTokens; i++)
        {
            var token = sampler.Sample(_llm.NativeHandle, batch.TokenCount - 1);
            if (token.IsEndOfGeneration(_weights.Vocab)) break;

            decoder.Add(token);
            batch.Clear();
            batch.Add(token, position++, LLamaSeqId.Zero, true);
            Decode(batch);
        }

        return new { ok = true, text = decoder.Read(), ms = clock.ElapsedMilliseconds };
    }

    /// <summary>
    /// A free answer to a whole chat - instructions, earlier turns and the
    /// question, already in the model's chat format - written out a few
    /// words at a time as it is made.
    /// </summary>
    /// <remarks>
    /// In a context of its own, so a chat never disturbs the reports
    /// assistant's reading of its instructions. Read afresh every time: a
    /// chat's instructions are a sentence, not a report list.
    /// </remarks>
    public object Chat(string modelPath, int threads, string prompt, int maxTokens,
        Action<string> write)
    {
        var clock = Stopwatch.StartNew();

        if (_weights is null || _modelPath != modelPath || _threads != threads)
        {
            Dispose();
            _modelPath = modelPath;
            _threads = threads;
            _weights = LLamaWeights.LoadFromFile(Parameters());
        }

        _chat?.Dispose();
        _chat = _weights.CreateContext(Parameters());

        var tokens = _chat.Tokenize(prompt, addBos: true, special: true);
        if (tokens.Length > ContextTokens - maxTokens)
            throw new InvalidOperationException("The conversation is too long; start a new one.");

        var batch = new LLamaBatch();
        var position = Read(_chat, tokens, 0, wantAnswer: true, batch);

        using var sampler = new DefaultSamplingPipeline
        {
            Temperature = 0.3f,
            RepeatPenalty = 1.1f
        };

        var decoder = new StreamingTokenDecoder(_chat);
        var produced = 0;

        for (; produced < maxTokens; produced++)
        {
            var token = sampler.Sample(_chat.NativeHandle, batch.TokenCount - 1);
            if (token.IsEndOfGeneration(_weights.Vocab)) break;

            decoder.Add(token);
            var text = decoder.Read();
            if (text.Length > 0) write(text);

            batch.Clear();
            batch.Add(token, position++, LLamaSeqId.Zero, true);
            Decode(_chat, batch);
        }

        return new { ok = true, done = true, tokens = produced, ms = clock.ElapsedMilliseconds };
    }

    private int Read(LLamaToken[] tokens, int start, bool wantAnswer, LLamaBatch batch) =>
        Read(_llm!, tokens, start, wantAnswer, batch);

    private void Decode(LLamaBatch batch) => Decode(_llm!, batch);

    private static void Decode(LLamaContext context, LLamaBatch batch)
    {
        var result = context.Decode(batch);
        if (result != DecodeResult.Ok)
            throw new InvalidOperationException($"The assistant's model stopped ({result}).");
    }

    /// <summary>
    /// Feeds tokens to the model in steps, from the given position, and
    /// returns the position after them.
    /// </summary>
    private static int Read(LLamaContext context, LLamaToken[] tokens, int start,
        bool wantAnswer, LLamaBatch batch)
    {
        var position = start;

        for (var i = 0; i < tokens.Length; i += ReadStep)
        {
            batch.Clear();
            var count = Math.Min(ReadStep, tokens.Length - i);

            for (var j = 0; j < count; j++)
                batch.Add(tokens[i + j], position++, LLamaSeqId.Zero,
                          wantAnswer && i + j == tokens.Length - 1);

            Decode(context, batch);
        }

        return position;
    }

    private bool TryLoad(string path)
    {
        if (!File.Exists(path)) return false;

        try
        {
            _llm!.LoadState(path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return true;
        }
#pragma warning disable CA1031 // A bad saved reading is read again, not fatal.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine($"Saved reading {path} could not be used: {ex.Message}");
            TryDelete(path);

            // Whatever half-loaded state is left goes with the context.
            _llm!.Dispose();
            _llm = _weights!.CreateContext(Parameters());
            return false;
        }
    }

    /// <summary>
    /// Writes the reading beside its final name and then moves it into
    /// place, so a PC switched off mid-write leaves no broken file.
    /// </summary>
    private void Save(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var partial = path + ".partial";
            _llm!.SaveState(partial);
            File.Move(partial, path, overwrite: true);

            foreach (var old in new DirectoryInfo(directory).GetFiles("*.state")
                         .OrderByDescending(f => f.LastWriteTimeUtc)
                         .Skip(KeptStates))
                TryDelete(old.FullName);
        }
#pragma warning disable CA1031 // Not saving only costs the next start its minute.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine($"Could not save the reading to {path}: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string CachePath(string directory, string instructions)
    {
        var model = new FileInfo(_modelPath);
        var key = $"{model.Name}|{model.Length}|{model.LastWriteTimeUtc.Ticks}|{instructions}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return Path.Combine(directory, hash.ToLowerInvariant() + ".state");
    }

    private ModelParams Parameters() => new(_modelPath)
    {
        ContextSize = ContextTokens,
        BatchSize = 512,
        GpuLayerCount = 0,
        Threads = _threads,
        BatchThreads = _threads
    };

    public void Dispose()
    {
        _afterInstructions?.Dispose();
        _afterInstructions = null;
        _llm?.Dispose();
        _llm = null;
        _chat?.Dispose();
        _chat = null;
        _weights?.Dispose();
        _weights = null;
    }
}
