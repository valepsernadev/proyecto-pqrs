using Microsoft.EntityFrameworkCore;

namespace SistemaPQRS.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
