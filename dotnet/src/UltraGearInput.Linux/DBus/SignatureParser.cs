namespace UltraGearInput.Linux.DBus;

internal static class SignatureParser
{
    public const int MaximumLength = 255;

    public static IReadOnlyList<string> Split(string signature)
    {
        if (signature.Length > MaximumLength)
        {
            throw new FormatException($"Signature is longer than {MaximumLength} characters.");
        }
        var types = new List<string>();
        for (var index = 0; index < signature.Length;)
        {
            var length = CompleteTypeLength(signature, index);
            types.Add(signature.Substring(index, length));
            index += length;
        }
        return types;
    }

    public static int CompleteTypeLength(string signature, int start)
    {
        if (start >= signature.Length)
        {
            throw new FormatException($"Signature '{signature}' ends early.");
        }
        return signature[start] switch
        {
            'a' => 1 + CompleteTypeLength(signature, start + 1),
            '(' => ContainerLength(signature, start, ')'),
            '{' => ContainerLength(signature, start, '}'),
            var code when IsBasic(code) || code == 'v' => 1,
            var code => throw new FormatException($"Unsupported type code '{code}' in '{signature}'."),
        };
    }

    public static int Alignment(char code) => code switch
    {
        'y' or 'g' or 'v' => 1,
        'n' or 'q' => 2,
        'b' or 'i' or 'u' or 's' or 'o' or 'a' or 'h' => 4,
        'x' or 't' or 'd' or '(' or '{' => 8,
        _ => throw new FormatException($"Unsupported type code '{code}'."),
    };

    public static IReadOnlyList<string> Members(string containerType) => Split(containerType[1..^1]);

    private static bool IsBasic(char code) => code is 'y' or 'b' or 'n' or 'q' or 'i' or 'u' or 'x' or 't' or 'd' or 's' or 'o' or 'g' or 'h';

    private static int ContainerLength(string signature, int start, char close)
    {
        var index = start + 1;
        while (index < signature.Length && signature[index] != close)
        {
            index += CompleteTypeLength(signature, index);
        }
        if (index >= signature.Length || index == start + 1)
        {
            throw new FormatException($"Container in '{signature}' is empty or not closed.");
        }
        return index - start + 1;
    }
}
