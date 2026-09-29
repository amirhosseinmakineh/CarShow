using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CarShow.Infrastracture.Context
{
    public class DesignTimeContextFactory : IDesignTimeDbContextFactory<CarShowContext>
    {
        public CarShowContext CreateDbContext(string[] args)
        {
            var builder = new DbContextOptionsBuilder<CarShowContext>();
            builder.UseSqlServer("Data Source = .;Initial Catalog = CarShow;Integrated Security = true;TrustServerCertificate=True");
            return new CarShowContext(builder.Options);
        }
    }
}
