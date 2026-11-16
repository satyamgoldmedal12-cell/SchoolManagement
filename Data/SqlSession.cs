using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;

namespace SchoolManagement.Data;

// One SQL connection/transaction shared by repositories and Identity during a request.
public sealed class SqlSession : IDisposable, IAsyncDisposable
{
    public SqlConnection Connection { get; }
    public SqlTransaction? Transaction { get; private set; }

    public SqlSession(SqlConnectionFactory connections)
    {
        Connection = connections.CreateConnection();
    }

    public CommandDefinition Command(string procedure, object? parameters = null, CancellationToken cancellationToken = default)
    {
        return new CommandDefinition(procedure, parameters, Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: cancellationToken);
    }

    public async Task<SqlWork> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is not null) throw new InvalidOperationException("A transaction is already active.");
        if (Connection.State != ConnectionState.Open) await Connection.OpenAsync(cancellationToken);
        Transaction = (SqlTransaction)await Connection.BeginTransactionAsync(cancellationToken);
        return new SqlWork(this, Transaction);
    }

    public async ValueTask DisposeAsync()
    {
        if (Transaction is not null) await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }

    public void Dispose()
    {
        Transaction?.Dispose();
        Connection.Dispose();
    }

    public sealed class SqlWork : IAsyncDisposable
    {
        private readonly SqlSession _session;
        private readonly SqlTransaction _transaction;
        internal SqlWork(SqlSession session, SqlTransaction transaction) { _session = session; _transaction = transaction; }
        public Task CommitAsync(CancellationToken cancellationToken = default) => _transaction.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default) => _transaction.RollbackAsync(cancellationToken);
        public async ValueTask DisposeAsync()
        {
            try { await _transaction.DisposeAsync(); }
            finally { _session.Transaction = null; }
        }
    }
}
