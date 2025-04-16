using Microsoft.EntityFrameworkCore;
using MvcBooks.Configuration;
using MvcBooks.Models;
using MvcBooks.Models.Data;
using Minio; 
using MvcBooks.Services;

var builder = WebApplication.CreateBuilder(args);


var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// --- ASP.NET Core Identity Configuration ---
builder.Services.AddDefaultIdentity<User>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 6;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>();


builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages(); 

builder.Services.Configure<MinioOptions>(
    builder.Configuration.GetSection(MinioOptions.Minio) 
);

builder.Services.AddSingleton<MinioService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); 

app.UseRouting();

app.UseAuthentication(); 
app.UseAuthorization();  

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages(); 

app.Run();