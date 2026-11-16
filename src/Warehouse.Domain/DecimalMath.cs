using System.Numerics;

namespace Warehouse.Domain;

internal static class DecimalMath
{
    private static readonly BigInteger Maximum = (BigInteger.One << 96) - 1;

    // Exact rational arithmetic avoids intermediate overflow and double rounding.
    // Retain up to 28 decimal places; round once, midpoint to even, as decimal does.
    public static decimal Proportion(decimal value, decimal part, decimal total)
    {
        var (v, vs) = Parts(value);
        var (p, ps) = Parts(part);
        var (t, ts) = Parts(total);
        var numerator = v * p * BigInteger.Pow(10, ts);
        var denominator = t * BigInteger.Pow(10, vs + ps);
        return Round(numerator, denominator);
    }

    public static decimal Sum(IEnumerable<decimal> values)
    {
        var sum = BigInteger.Zero;
        foreach (var value in values)
        {
            var (coefficient, scale) = Parts(value);
            sum += coefficient * BigInteger.Pow(10, 28 - scale);
        }
        return Round(sum, BigInteger.Pow(10, 28));
    }

    private static decimal Round(BigInteger numerator, BigInteger denominator)
    {
        var negative = numerator.Sign < 0;
        numerator = BigInteger.Abs(numerator);
        for (var scale = 28; scale >= 0; scale--)
        {
            var quotient = BigInteger.DivRem(numerator * BigInteger.Pow(10, scale), denominator, out var remainder);
            var comparison = (remainder * 2).CompareTo(denominator);
            if (comparison > 0 || comparison == 0 && !quotient.IsEven) quotient++;
            if (quotient > Maximum) continue;
            return new decimal(unchecked((int)(uint)(quotient & uint.MaxValue)),
                unchecked((int)(uint)((quotient >> 32) & uint.MaxValue)),
                unchecked((int)(uint)((quotient >> 64) & uint.MaxValue)), negative, (byte)scale);
        }
        throw new WarehouseException(ErrorCode.Validation, "Decimal range exceeded.");
    }

    private static (BigInteger Coefficient, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        return ((bits[3] & int.MinValue) == 0 ? coefficient : -coefficient, (bits[3] >> 16) & 0xff);
    }
}
