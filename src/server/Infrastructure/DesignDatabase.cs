using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Learning;
public class DesignDatabase : IDesignTimeDbContextFactory<Database>
{
    public Database CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<Database>().UseNpgsql("Host=127.0.0.1;Port=55432;Database=learning;Username=qianjundeng").Options);
}
