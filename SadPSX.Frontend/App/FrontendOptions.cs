namespace SadPSX.Frontend.App;

internal sealed record FrontendOptions(
    string BiosPath,
    int InstructionBatchSize,
    bool StartPaused,
    int? FrameLimit)
{
    public static FrontendOptions Parse(string[] arguments)
    {
        if (arguments.Length == 0)
            throw new ArgumentException("Informe o caminho da BIOS.");

        string? biosPath = null;
        int instructionBatchSize = 10_000;
        bool startPaused = false;
        int? frameLimit = null;

        for (int argumentIndex = 0;
             argumentIndex < arguments.Length;
             argumentIndex++)
        {
            string argument = arguments[argumentIndex];
            switch (argument)
            {
                case "--batch":
                    instructionBatchSize = ParsePositiveInteger(
                        ReadValue(arguments, ref argumentIndex, argument),
                        argument);
                    break;

                case "--paused":
                    startPaused = true;
                    break;

                case "--frames":
                    frameLimit = ParsePositiveInteger(
                        ReadValue(arguments, ref argumentIndex, argument),
                        argument);
                    break;

                default:
                    if (argument.StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException(
                            $"Opção desconhecida: {argument}.");

                    if (biosPath is not null)
                        throw new ArgumentException(
                            "Apenas um caminho de BIOS pode ser informado.");

                    biosPath = argument;
                    break;
            }
        }

        if (biosPath is null)
            throw new ArgumentException("Informe o caminho da BIOS.");

        return new FrontendOptions(
            Path.GetFullPath(biosPath),
            instructionBatchSize,
            startPaused,
            frameLimit);
    }

    private static string ReadValue(
        string[] arguments,
        ref int argumentIndex,
        string option)
    {
        if (argumentIndex + 1 >= arguments.Length)
            throw new ArgumentException($"A opção {option} exige um valor.");

        argumentIndex++;
        return arguments[argumentIndex];
    }

    private static int ParsePositiveInteger(string value, string option)
    {
        if (!int.TryParse(value, out int result) || result <= 0)
        {
            throw new ArgumentException(
                $"Valor inválido para {option}: {value}.");
        }

        return result;
    }
}
