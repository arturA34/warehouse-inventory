using System.Numerics;

namespace Warehouse.Domain;

internal static class DecimalMath
{
    private static readonly BigInteger Maximum = (BigInteger.One << 96) - 1;

    // При списании сначала считаем дробь целиком, потом один раз округляем её до decimal.
    public static decimal Proportion(decimal value, decimal part, decimal total)
    {
        var (amount, amountScale) = Parts(value);
        var (quantity, quantityScale) = Parts(part);
        var (stock, stockScale) = Parts(total);
        var numerator = amount * quantity * BigInteger.Pow(10, stockScale);
        var denominator = stock * BigInteger.Pow(10, amountScale + quantityScale);
        return Round(numerator, denominator);
    }

    public static decimal Sum(IEnumerable<decimal> values)
    {
        var sum = BigInteger.Zero;
        var scale = 0;
        foreach (var value in values)
        {
            var (coefficient, valueScale) = Parts(value);
            if (valueScale > scale)
            {
                sum *= BigInteger.Pow(10, valueScale - scale);
                scale = valueScale;
            }
            sum += coefficient * BigInteger.Pow(10, scale - valueScale);
        }
        // Остаток должен совпасть с движениями до последнего знака.
        // Убирать можно только нули в конце дробной части.
        while (BigInteger.Abs(sum) > Maximum && scale > 0 && sum % 10 == 0)
        {
            sum /= 10;
            scale--;
        }
        Rules.Require(BigInteger.Abs(sum) <= Maximum, ErrorCode.Validation, "Sum cannot be represented exactly.");
        return ToDecimal(BigInteger.Abs(sum), sum.Sign < 0, scale);
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
            return ToDecimal(quotient, negative, scale);
        }
        throw new WarehouseException(ErrorCode.Validation, "Decimal range exceeded.");
    }

    private static decimal ToDecimal(BigInteger magnitude, bool negative, int scale)
    {
        int Word(int shift) => unchecked((int)(uint)((magnitude >> shift) & uint.MaxValue));
        return new decimal(Word(0), Word(32), Word(64), negative, (byte)scale);
    }

    private static (BigInteger Coefficient, int Scale) Parts(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        return ((bits[3] & int.MinValue) == 0 ? coefficient : -coefficient, (bits[3] >> 16) & 0xff);
    }
}
