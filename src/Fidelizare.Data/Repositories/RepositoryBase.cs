using Microsoft.Data.Sqlite;

namespace Fidelizare.Data.Repositories;

/// <summary>
/// Bază comună pentru repository-uri: toate lucrează pe conexiunea și tranzacția
/// unității de lucru care le-a creat, deci modificările lor se confirmă împreună.
/// </summary>
internal abstract class RepositoryBase
{
    protected RepositoryBase(SqliteConnection connection, SqliteTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    protected SqliteConnection Connection { get; }

    protected SqliteTransaction Transaction { get; }

    protected SqliteCommand Command(string sql)
    {
        var command = Connection.CreateCommand();
        command.Transaction = Transaction;
        command.CommandText = sql;
        return command;
    }

    /// <summary>Rulează un INSERT care se termină cu „RETURNING Id” și întoarce identificatorul nou.</summary>
    protected static int ExecuteInsert(SqliteCommand command)
    {
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    protected static int ScalarInt(SqliteCommand command)
    {
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    protected static long ScalarLong(SqliteCommand command)
    {
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0L : Convert.ToInt64(value);
    }
}
