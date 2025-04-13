using Microsoft.AspNetCore.Identity.UI;
using Microsoft.EntityFrameworkCore;
using MvcBooks.Models;
using MvcBooks.Models.Data; // Make sure SeedData is in this namespace or add using
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// --- Database Context Configuration ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// --- ASP.NET Core Identity Configuration ---
// Switched to AddDefaultIdentity for convenience (includes cookie setup)
builder.Services.AddDefaultIdentity<User>(options =>
    {
        // Configure Identity options (Examples):
        options.SignIn.RequireConfirmedAccount = false; // START WITH FALSE for easier testing
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireLowercase = false; // Adjust as needed
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>(); // Still uses your DbContext

// --- MVC & Razor Pages Configuration ---
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); // *** ADDED: Required for Identity UI ***

var app = builder.Build();

// --- Seed Data (Keep this logic) ---
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        // Ensure database exists/is migrated before seeding
        // Option 1: Apply Migrations (Recommended)
        // await context.Database.MigrateAsync();
        // Option 2: Ensure Created (Simpler for initial dev, careful!)
        // context.Database.EnsureCreated();

        // Call the seed data initializer
        await SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred migrating or seeding the DB.");
    }
}


// --- Configure the HTTP request pipeline ---
if (!app.Environment.IsDevelopment())
{
    
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// *** MOVED UseStaticFiles before UseRouting (Convention) ***
app.UseStaticFiles(); // Serve static files like CSS, JS, images

app.UseRouting();

// *** ADDED/ORDERED Authentication/Authorization Middleware ***
app.UseAuthentication();   // <<< ADDED: Authenticates the user (reads cookie etc.)
app.UseAuthorization();  // <<< MUST come AFTER UseAuthentication

// Assuming these are for your specific setup (e.g., .NET 9 SPA template?)
// If not related to SPA proxying, standard static files middleware is usually sufficient.
// app.MapStaticAssets(); // Keep if needed for your setup

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
    // .WithStaticAssets(); // Keep if needed for your setup

app.MapRazorPages(); // *** ADDED: Maps Identity UI routes like /Identity/Account/Login ***

app.Run();