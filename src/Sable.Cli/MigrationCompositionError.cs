// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

namespace Sable.Cli;

internal static class MigrationCompositionError
{
    private const string Prefix = "Sable cannot safely wrap migration '";

    public static InvalidOperationException For(string migrationId)
    {
        return new InvalidOperationException(
            $"{Prefix}{migrationId}' because it begins with an unsupported top-level DO statement. Only a recognized safe schema-creation preamble can be moved outside the idempotence guard. Use Sable's no-idempotence workflow only when the migration is independently idempotent."
        );
    }

    public static bool Is(Exception exception)
    {
        return exception is InvalidOperationException
            && exception.Message.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public static int WriteAndReturnExitCode(Exception exception)
    {
        System.Console.Error.WriteLine($"ERROR: {exception.Message}");
        Environment.ExitCode = 1;
        return 1;
    }
}
