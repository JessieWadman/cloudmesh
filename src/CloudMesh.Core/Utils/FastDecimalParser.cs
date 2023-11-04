using System.Globalization;
using System.Runtime.CompilerServices;

namespace System
{
    public record DecimalSeparators(char ThousandSeparator, char DecimalSeparator)
    {
        public static readonly DecimalSeparators EN_US = new(',', '.');
        public static readonly DecimalSeparators SV_SE = new(' ', ',');
        public static readonly DecimalSeparators ISO = new(' ', '.');
    }

    public static partial class OptimizationHelpers
    {
        private const int MaxDecimalLength = 29 + 1 + 29 + 9; // 29 integral digits, decimal separator, 28-29 decimal digits and 9 group separators

        private static readonly int[] PowerOf10 = {
            1,
            10,
            100,
            1000,
            10000,
            100000,
            1000000,
            10000000,
            100000000,
            1000000000
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe bool FastTryParseDecimal(ReadOnlySpan<char> value, DecimalSeparators separators, out decimal parsedValue)
        {
            var sourceLength = value.Length;
            if (sourceLength == 0)
            {
                parsedValue = default;
                return false;
            }

            if (sourceLength > MaxDecimalLength)
            {
                parsedValue = default;
                return false;
            }

            // FastTryParseDecimalImpl handles EN_US, if we're using some other decimal/thousand separators,
            // we first trim and convert it to EN_US, then parse it.
            if (separators.DecimalSeparator != '.' || separators.ThousandSeparator != ' ')
            {
                fixed (char* sourceStart = value)
                {
                    var outputBufferPtr = stackalloc char[sourceLength + 1];
                    var currentSourcePtr = sourceStart;
                    var currentOutputBufferPtr = outputBufferPtr;

                    for (var i = 0; i < sourceLength; i++, currentSourcePtr++)
                    {
                        if (*currentSourcePtr == separators.ThousandSeparator || *currentSourcePtr == ' ')
                            continue;

                        if (*currentSourcePtr == separators.DecimalSeparator)
                        {
                            *currentOutputBufferPtr = '.';
                            currentOutputBufferPtr++;
                        }
                        else
                        {
                            *currentOutputBufferPtr = *currentSourcePtr;
                            currentOutputBufferPtr++;
                        }
                    }

                    var remainderLength = (int)(currentOutputBufferPtr - outputBufferPtr);
                    if (remainderLength == 0)
                    {
                        parsedValue = 0;
                        return false;
                    }

                    var remainder = new ReadOnlySpan<char>(outputBufferPtr, remainderLength);
                    return FastTryParseDecimalImpl(remainder, remainderLength, out parsedValue);
                }
            }

            sourceLength = value.Length;

            return FastTryParseDecimalImpl(value, sourceLength, out parsedValue);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool FastTryParseDecimalImpl(ReadOnlySpan<char> source, int sourceLength, out decimal result)
            {
                if (sourceLength != 0)
                {
                    var negative = false;
                    long n = 0;
                    var start = 0;
                    if (source[0] == '-')
                    {
                        negative = true;
                        start = 1;
                    }

                    if (sourceLength <= 19)
                    {
                        var decpos = sourceLength;
                        for (var k = start; k < sourceLength; k++)
                        {
                            var c = source[k];
                            switch (c)
                            {
                                case '.':
                                    decpos = k + 1;
                                    break;
                                case ' ':
                                    continue;
                                case < '0':
                                case > '9':
                                    result = 0;
                                    return false;
                                default:
                                    n = (n * 10) + (int)(c - '0');
                                    break;
                            }
                        }
                        result = new decimal((int)n, (int)(n >> 32), 0, negative, (byte)(sourceLength - decpos));
                        return true;
                    }
                    else
                    {
                        if (sourceLength > 28)
                        {
                            sourceLength = 28;
                        }
                        var decpos = sourceLength;
                        for (var k = start; k < 19; k++)
                        {
                            var c = source[k];
                            switch (c)
                            {
                                case '.':
                                    decpos = k + 1;
                                    break;
                                case ' ':
                                    continue;
                                case < '0':
                                case > '9':
                                    result = 0;
                                    return false;
                                default:
                                    n = (n * 10) + (int)(c - '0');
                                    break;
                            }
                        }
                        var n2 = 0;
                        var secondhalfdec = false;
                        for (var k = 19; k < sourceLength; k++)
                        {
                            var c = source[k];
                            switch (c)
                            {
                                case '.':
                                    decpos = k + 1;
                                    secondhalfdec = true;
                                    break;
                                case ' ':
                                    continue;
                                case < '0':
                                case > '9':
                                    result = 0;
                                    return false;
                                default:
                                    n2 = (n2 * 10) + (int)(c - '0');
                                    break;
                            }
                        }
                        var decimalPosition = (byte)(sourceLength - decpos);
                        result = new decimal((int)n, (int)(n >> 32), 0, negative, decimalPosition) * PowerOf10[sourceLength - (!secondhalfdec ? 19 : 20)] + new decimal(n2, 0, 0, negative, decimalPosition);
                        return true;
                    }
                }
                result = 0;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FastTryParseDecimal(ReadOnlySpan<char> value, out decimal parsedValue)
            => FastTryParseDecimal(value, DecimalSeparators.ISO, out parsedValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool FastTryParseDecimal(ReadOnlySpan<char> value, CultureInfo culture, out decimal parsedValue)
        {
            if (culture.Equals(CultureInfo.InvariantCulture))
                return FastTryParseDecimal(value, out parsedValue);
            
            var thousandSeparator = culture.NumberFormat.NumberGroupSeparator.Length switch
            {
                0 => '\x255',
                _ => culture.NumberFormat.NumberGroupSeparator[0]
            };

            var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator.Length switch
            {
                0 => '\x254',
                _ => culture.NumberFormat.NumberDecimalSeparator[0]
            };
            
            return FastTryParseDecimal(
                value,
                new DecimalSeparators(thousandSeparator, decimalSeparator), 
                out parsedValue);
        }
    }
}
