using System.Security.Cryptography;
using System.Text;
using CsCheck;

namespace QueenZone.TestUtilities;

public static class PropertyRun
{
    public const int DefaultIterations = 100;
    public const int CheapIterations = 1_000;
    public static readonly TimeSpan DefaultIterationTimeout = TimeSpan.FromSeconds(2);

    public static void Run<T>(string propertyName, Gen<T> gen, Action<T> assert, int iter = DefaultIterations)
    {
        RunAsync(propertyName, gen, t =>
        {
            assert(t);
            return Task.CompletedTask;
        }, iter).GetAwaiter().GetResult();
    }

    public static async Task RunAsync<T>(string propertyName, Gen<T> gen, Func<T, Task> assert,
        int iter = DefaultIterations, TimeSpan? iterationTimeout = null, CancellationToken cancellationToken = default)
    {
        var seed = ResolveSeed(propertyName);
        var pcg = PCG.Parse(seed);
        Size? minSize = null;
        ulong minState = 0;
        T minT = default!;
        Exception? minException = null;
        var shrinks = -1;
        var genThrew = false;

        for (var i = 0; i < iter; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var state = pcg.State;
            Size? size = null;
            T t = default!;
            var generating = true;
            try
            {
                t = gen.Generate(pcg, minSize, out size);
                generating = false;
                if (minSize is null || Size.IsLessThan(size, minSize))
                {
                    await AwaitAssertionAsync(assert, t, iterationTimeout ?? DefaultIterationTimeout, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (ArgumentNullException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (!generating && (minSize is null || Size.IsLessThan(size, minSize)))
                {
                    minSize = size;
                    minState = state;
                    minT = t;
                    minException = exception;
                    genThrew = false;
                    shrinks++;
                    if (exception is TimeoutException)
                    {
                        break;
                    }
                }
                else if (generating && minException is null)
                {
                    minState = state;
                    minException = exception;
                    genThrew = true;
                }
            }
        }

        if (minException is not null)
        {
            throw Failure(propertyName, seed, pcg.ToString(minState), genThrew, minT, shrinks, minException);
        }
    }

    private static string ResolveSeed(string propertyName)
    {
        var seed = Environment.GetEnvironmentVariable("QZ_PROPERTY_SEED");
        return string.IsNullOrEmpty(seed)
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(propertyName)))[..16]
            : seed;
    }

    private static async Task AwaitAssertionAsync<T>(Func<T, Task> assert, T value,
        TimeSpan iterationTimeout, CancellationToken cancellationToken)
    {
        var task = assert(value);
        ArgumentNullException.ThrowIfNull(task);
        await task.WaitAsync(iterationTimeout, cancellationToken).ConfigureAwait(false);
    }

    private static InvalidOperationException Failure<T>(string propertyName, string initialSeed,
        string seed, bool genThrew, T value, int shrinks, Exception exception)
    {
        var input = genThrew ? "The generator threw." : Check.Print(value);
        return new InvalidOperationException(
            $"Property '{propertyName}' (initial seed: {initialSeed}):\nseed: {seed}\n{input}\nShrinks: {Math.Max(0, shrinks)}\n{exception.Message}",
            exception);
    }
}
