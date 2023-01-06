using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    public record DecimalSeparators(char ThousandSeparator, char DecimalSeparator)
    {
        public static readonly DecimalSeparators EN_US = new(',', '.');
        public static readonly DecimalSeparators SV_SE = new(' ', ',');
        public static readonly DecimalSeparators ISO = new(' ', '.');
    }

    public static class OptimizationHelpers
    {
        private const int MaxDecimalLength = 29 + 1 + 29 + 9; // 29 integral digits, decimal separator, 28-29 decimal digits and 9 group separators

        private static int[] powof10 = new int[10]
        {
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
        public static bool FastTryParseDecimal(ReadOnlySpan<char> value, DecimalSeparators separators, out decimal parsedValue)
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

            if (separators.DecimalSeparator != '.' || separators.ThousandSeparator != ' ')
                value = UnsafeTrimAndConvertToInvariant(value, separators.ThousandSeparator, separators.DecimalSeparator, sourceLength);
            sourceLength = value.Length;

            return FastTryParseDecimalImpl(value, sourceLength, out parsedValue);

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static unsafe string UnsafeTrimAndConvertToInvariant(ReadOnlySpan<char> source, char thousandSeparator, char decimalSeparator, int sourceLength)
            {
                fixed (char* sourceStart = source)
                {
                    var outputBufferPtr = stackalloc char[sourceLength + 1];
                    char* currentSourcePtr = sourceStart;
                    char* currentOutputBufferPtr = outputBufferPtr;

                    for (int i = 0; i < sourceLength; i++, currentSourcePtr++)
                    {
                        if (*currentSourcePtr == thousandSeparator || *currentSourcePtr == ' ')
                            continue;

                        if (*currentSourcePtr == decimalSeparator)
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
                        return string.Empty;
                    var remainder = new string(outputBufferPtr, 0, remainderLength);
                    return remainder;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool FastTryParseDecimalImpl(ReadOnlySpan<char> source, int sourceLength, out decimal result)
            {
                if (sourceLength != 0)
                {
                    bool negative = false;
                    long n = 0;
                    int start = 0;
                    if (source[0] == '-')
                    {
                        negative = true;
                        start = 1;
                    }

                    if (sourceLength <= 19)
                    {
                        int decpos = sourceLength;
                        for (int k = start; k < sourceLength; k++)
                        {
                            char c = source[k];
                            if (c == '.')
                            {
                                decpos = k + 1;
                            }
                            else if (c == ' ')
                            {
                                continue;
                            }
                            else if (c < '0' || c > '9')
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                n = (n * 10) + (int)(c - '0');
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
                        int decpos = sourceLength;
                        for (int k = start; k < 19; k++)
                        {
                            char c = source[k];
                            if (c == '.')
                            {
                                decpos = k + 1;
                            }
                            else if (c == ' ')
                            {
                                continue;
                            }
                            else if (c < '0' || c > '9')
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                n = (n * 10) + (int)(c - '0');
                            }
                        }
                        int n2 = 0;
                        bool secondhalfdec = false;
                        for (int k = 19; k < sourceLength; k++)
                        {
                            char c = source[k];
                            if (c == '.')
                            {
                                decpos = k + 1;
                                secondhalfdec = true;
                            }
                            else if (c == ' ')
                            {
                                continue;
                            }
                            else if (c < '0' || c > '9')
                            {
                                result = 0;
                                return false;
                            }
                            else
                            {
                                n2 = (n2 * 10) + (int)(c - '0');
                            }
                        }
                        byte decimalPosition = (byte)(sourceLength - decpos);
                        result = new decimal((int)n, (int)(n >> 32), 0, negative, decimalPosition) * powof10[sourceLength - (!secondhalfdec ? 19 : 20)] + new decimal(n2, 0, 0, negative, decimalPosition);
                        return true;
                    }
                }
                result = 0;
                return false;
            }
        }
    }
}
