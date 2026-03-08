using AuthenticationTest.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuthenticationTest.Data
{
    public class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
    {
        // This DbSet represents the Users table in the database
        public DbSet<User> Users { get; set; }
    }
}
