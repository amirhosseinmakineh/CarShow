using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore.Storage;

public class UnitOfWork : IUnitOfWork
{
    private readonly CarShowContext _context;

    public UnitOfWork(CarShowContext context)
    {
        _context = context;
    }

    private IDbContextTransaction? _transaction;

    public async Task BeginTransactionAsync()
    {
        _transaction = await _context.Database.BeginTransactionAsync();
    }

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public async Task CommitAsync()
    {
        var transaction = _transaction ?? throw new InvalidOperationException("No active transaction to commit.");
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        _transaction = null;
        await transaction.DisposeAsync();
    }

    public async Task RollbackAsync()
    {
        var transaction = _transaction;
        if (transaction is null)
            return;

        _transaction = null;
        try
        {
            // SQL Server can end the transaction before the application sees the failure.
            if (transaction.GetDbTransaction().Connection is not null)
                await transaction.RollbackAsync();
        }
        finally
        {
            _context.ChangeTracker.Clear();
            await transaction.DisposeAsync();
        }
    }
}
