using CarShow.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace CarShow.Infrastracture.Context
{
    public class CarShowContext : DbContext
    {
        public CarShowContext(DbContextOptions<CarShowContext> options) : base(options)
        {

        }
        public DbSet<User> Users { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Car> Cars { get; set; }
        public DbSet<Tip> Tips { get; set; }
        public DbSet<Model> CarModels { get; set; }
        public DbSet<Company> Companies { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
    }
}
