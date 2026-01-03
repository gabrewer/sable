// Copyright 2024 Bloomberg Finance L.P.
// Distributed under the terms of the MIT license.

namespace Sable.Cli.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Converts a database name to a cleaned version suitable for use in file system paths.
    /// </summary>
    /// <param name="databaseName">The database name to clean.</param>
    /// <returns>A cleaned version of the database name with invalid file system characters replaced.</returns>
    public static string ToDatabasePathName(this string databaseName)
    {
        if (databaseName == SableCliConstants.DefaultDatabaseName)
        {
            return "Marten";
        }

        if (databaseName == SableCliConstants.DefaultWolverineName)
        {
            return "WolverineEnvelopeStorage";
        }

        return databaseName
            .Replace("://", "_")
            .Replace("/", "_")
            .Replace("\\", "_")
            .Replace(":", "_")
            .Replace("*", "_");
    }
}
