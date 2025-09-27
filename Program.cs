using DotNetEnv;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

// Load connection string from .env
string connectionString = Environment.GetEnvironmentVariable("POSTGRES_URL")
    ?? throw new InvalidOperationException("POSTGRES_URL environment variable not set");

// Register DbContext
builder.Services.AddDbContext<MyDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// ✅ Add this simple health check endpoint
app.MapGet("/", () => "✅ MoneyView API is running...");

// ✅ Your existing cashKuber endpoint
app.MapPost("/cashKuber", async (List<MoneyViewUser> users, MyDbContext db, HttpContext http) =>
{
    // API Key validation
    if (!http.Request.Headers.TryGetValue("api-key", out var apiKey) || apiKey != "moneyview")
        return Results.Json(new { message = "Unauthorized: Invalid api-key header" }, statusCode: 401);

    if (users == null || users.Count == 0)
        return Results.Json(new { message = "No users provided" }, statusCode: 400);

    var inserted = new List<object>();
    var skipped = new List<object>();

    foreach (var user in users)
    {
        if (string.IsNullOrWhiteSpace(user.PartnerId))
        {
            skipped.Add(new { user.Name, user.Phone, user.Pan, reason = "Missing PartnerId" });
            continue;
        }

        if (string.IsNullOrWhiteSpace(user.Phone) && string.IsNullOrWhiteSpace(user.Pan))
        {
            skipped.Add(new { user.Name, reason = "Missing Phone and PAN" });
            continue;
        }

        // Check for duplicates
        bool exists = await db.MoneyViewUsers
            .AnyAsync(u => u.Phone == user.Phone || u.Pan == user.Pan);

        if (exists)
        {
            skipped.Add(new { user.Name, user.Phone, user.Pan, reason = "Duplicate phone or PAN" });
            continue;
        }

        try
        {
            db.MoneyViewUsers.Add(user);
            await db.SaveChangesAsync();

            inserted.Add(new
            {
                user.Name,
                user.Phone,
                user.Pan,
                status = "Inserted",
                createdDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")
            });
        }
        catch (Exception ex)
        {
            skipped.Add(new { user.Name, user.Phone, user.Pan, reason = ex.Message });
        }
    }

    if (inserted.Count > 0 && skipped.Count > 0)
        return Results.Json(new { insertedCount = inserted.Count, skippedCount = skipped.Count, inserted, skipped }, statusCode: 207);

    if (inserted.Count == 0 && skipped.Count > 0)
        return Results.Json(new { skippedCount = skipped.Count, skipped }, statusCode: 409);

    return Results.Json(new { insertedCount = inserted.Count, inserted }, statusCode: 200);
});

app.Run();

public class MyDbContext : DbContext
{
    public MyDbContext(DbContextOptions<MyDbContext> options) : base(options) { }

    public DbSet<MoneyViewUser> MoneyViewUsers => Set<MoneyViewUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MoneyViewUser>(entity =>
        {
            entity.ToTable("moneyview");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Employment).HasColumnName("employment");
            entity.Property(e => e.Pan).HasColumnName("pan");
            entity.Property(e => e.Pincode).HasColumnName("pincode");
            entity.Property(e => e.Income).HasColumnName("income");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.State).HasColumnName("state");
            entity.Property(e => e.Dob).HasColumnName("dob");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.PartnerId).HasColumnName("partner_id").IsRequired();
        });
    }
}

public class MoneyViewUser
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Employment { get; set; }
    public string? Pan { get; set; }
    public string? Pincode { get; set; }
    public string? Income { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Dob { get; set; }
    public string? Gender { get; set; }
    public string PartnerId { get; set; } = default!;
}