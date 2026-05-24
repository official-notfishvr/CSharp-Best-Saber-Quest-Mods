using CoreMod;

namespace RegressionMod;

public class DummyTarget
{
    public virtual int Compute(int input) => input;
}

[Mod("com.example.regressionmod", "1.0.0")]
public static class RegressionHooks
{
    [Hook(typeof(DummyTarget), nameof(DummyTarget.Compute))]
    public static int OnCompute(DummyTarget self, int input)
    {
        var result = UseSwitchAndLoops(input);
        Increment(ref result);
        return result + SumSeededArray(result);
    }

    private static int UseSwitchAndLoops(int input)
    {
        var total = 0;
        var index = 0;

        do
        {
            switch ((input + index) % 4)
            {
                case 0:
                    total += index;
                    break;
                case 1:
                    index++;
                    continue;
                case 2:
                    total += input;
                    break;
                default:
                    total -= 1;
                    break;
            }

            index++;
        } while (index < 5);

        return total;
    }

    private static void Increment(ref int value)
    {
        value += 2;
    }

    private static int SumSeededArray(int seed)
    {
        var values = new int[3];
        for (var i = 0; i < values.Length; i++)
            values[i] = seed + i;

        var total = 0;
        foreach (var value in values)
            total += value;

        return total;
    }
}
