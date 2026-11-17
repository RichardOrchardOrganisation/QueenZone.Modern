using System.Security.Cryptography;
using System.Text;
using CsCheck;

namespace QueenZone.TestUtilities;

public static class PropertyRun
{
    public const int DefaultIterations = 100;
    public const int CheapIterations = 1_000;

    public static void Run<T>(string propertyName, Gen<T> gen, Action<T> assert, int iter = DefaultIterations)
    {
        var seed = Environment.GetEnvironmentVariable("QZ_PROPERTY_SEED");
        if (string.IsNullOrEmpty(seed))
        {
            seed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(propertyName)))[..16];
        }

        var pcg = PCG.Parse(seed);
        Size? minSize = null;
        ulong minState = 0;
        T minT = default!;
        Exception? minException = null;
        var shrinks = -1;
        var genThrew = false;

        for (var i = 0; i < iter; i++)
        {
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
                    assert(t);
                }
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
            var input = genThrew ? "The generator threw." : Check.Print(minT);
            throw new InvalidOperationException(
                $"Property '{propertyName}' (initial seed: {seed}):\nseed: {pcg.ToString(minState)}\n{input}\nShrinks: {Math.Max(0, shrinks)}\n{minException.Message}",
                minException);
        }
    }
}
