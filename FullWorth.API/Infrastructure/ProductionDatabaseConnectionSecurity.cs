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
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "The production database connection string is invalid.",
                exception);
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
