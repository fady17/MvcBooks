using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MvcBooks.Models;
using Microsoft.EntityFrameworkCore;
using MvcBooks.Models.Data;
using System.Linq;         
using System.Threading.Tasks;

namespace MvcBooks.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly ApplicationDbContext _context;

    public HomeController(ILogger<HomeController> logger, ApplicationDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var categoriesWithBooks = await _context.Categories
                                            .OrderBy(c => c.DisplayOrder) // Order categories
                                            .Include(c => c.Books)        // Eager load books for each category
                                            .ToListAsync();

            // Pass the list of categories (which now contain their books) to the view
            return View(categoriesWithBooks);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
