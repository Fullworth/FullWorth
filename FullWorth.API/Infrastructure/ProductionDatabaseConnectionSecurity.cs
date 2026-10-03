using Npgsql;

namespace FullWorth.API.Infrastructure;

internal static class ProductionDatabaseConnectionSecurity
{
    private const string InternalProductionDatabaseHost =
        "database";

    public static void Validate(
        string connectionString,
        bool isDevelopment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            connectionString);

        if (isDevelopment)
        {
            return;
        }

        NpgsqlConnectionStringBuilder connectionBuilder;

        try
        {
            connectionBuilder =
                new NpgsqlConnectionStringBuilder(
                    connectionString);
        }
        catch (ArgumentException)
        {
            /*
             * Do not preserve the parser exception as an inner exception.
             * Connection-string parser failures are configuration errors, and
             * the original exception is not required for recovery. Keeping a
             * secret-bearing input anywhere in the exception chain would make
             * accidental diagnostic disclosure harder to reason about.
             */
            throw new InvalidOperationException(
                "The production database connection string is invalid.");
        }

        if (string.IsNullOrWhiteSpace(
                connectionBuilder.Host))
        {
            throw new InvalidOperationException(
                "The production database connection must specify an explicit host.");
        }

        if (string.IsNullOrWhiteSpace(
                connectionBuilder.Username))
        {
            throw new InvalidOperationException(
                "The production database connection must specify an explicit username.");
        }

        if (string.IsNullOrWhiteSpace(
                connectionBuilder.Password))
        {
            throw new InvalidOperationException(
                "The production database connection must use explicit password authentication.");
        }

        var databaseHost =
            connectionBuilder.Host.Trim();

        if (string.Equals(
                databaseHost,
                InternalProductionDatabaseHost,
                StringComparison.OrdinalIgnoreCase))
        {
            /*
             * The production Compose database is reachable only through the
             * internal Docker data network and is not published to the host.
             * Keep that existing private-network path compatible while the
             * database runtime/migration-role separation is implemented.
             */
            return;
        }

        if (connectionBuilder.SslMode !=
            SslMode.VerifyFull)
        {
            throw new InvalidOperationException(
                "A non-local production database connection must use SSL Mode=VerifyFull.");
        }
    }
}
