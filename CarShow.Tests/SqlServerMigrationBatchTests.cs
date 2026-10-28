using CarShow.Infrastracture.Context;
using CarShow.Infrastracture.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace CarShow.Tests;

public sealed class SqlServerMigrationBatchTests
{
    [Theory]
    [InlineData(typeof(SmartTelegramComparisons))]
    [InlineData(typeof(TelegramStrictComparisons))]
    [InlineData(typeof(ReconcileTelegramModelSnapshot))]
    public void NewColumnAndDependentIndexAreSentAsSeparateCommands(Type migrationType)
    {
        using var db = new DesignTimeContextFactory().CreateDbContext([]);
        var migration = (Migration)Activator.CreateInstance(migrationType)!;
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model);
        Assert.True(commands.Count >= 2);
        Assert.All(commands, command => Assert.False(
            command.CommandText.Contains("ALTER TABLE", StringComparison.OrdinalIgnoreCase)
            && command.CommandText.Contains("CREATE ", StringComparison.OrdinalIgnoreCase),
            "New columns and dependent indexes must not share one SQL Server compilation batch."));
    }
}
