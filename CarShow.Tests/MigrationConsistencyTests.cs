using CarShow.Infrastracture.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CarShow.Tests;

public sealed class MigrationConsistencyTests
{
    [Fact]
    public void SqlServerModelMatchesLatestMigrationSnapshot()
    {
        // Builds the SQL Server model without opening a database connection.
        using var db = new DesignTimeContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
