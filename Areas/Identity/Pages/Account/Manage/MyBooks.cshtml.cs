using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MvcBooks.Models; // Reference your models
using MvcBooks.Models.Data; // Reference your DbContext

namespace MvcBooks.Areas.Identity.Pages.Account.Manage
{
    public class MyBooksModel : PageModel
    {
        private readonly UserManager<User> _userManager;
        private readonly ApplicationDbContext _context;

        public MyBooksModel(
            UserManager<User> userManager,
            ApplicationDbContext context)
        {
            _userManager = userManager;
            _context = context;
        }

        // Property to hold the list of books for the view
        public List<Book> UserBooks { get; set; } = new List<Book>();

        [TempData]
        public string? StatusMessage { get; set; } // To display messages from controller redirects

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                // Should not happen if user is authenticated, but good practice
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            // Fetch books specifically uploaded by the current user
            UserBooks = await _context.Books
                                .Where(b => b.UserId == user.Id)
                                .OrderByDescending(b => b.PublishedDate) // Example sorting: newest first
                                // Optionally include categories if you plan to display them
                                // .Include(b => b.Categories) 
                                .ToListAsync();
            
            // Copy TempData messages from controller redirects if they exist
            if (TempData.ContainsKey("SuccessMessage"))
            {
                 StatusMessage = TempData["SuccessMessage"]?.ToString();
            }
            else if (TempData.ContainsKey("ErrorMessage"))
            {
                 // Use a different styling or prefix for errors if needed
                 StatusMessage = $"Error: {TempData["ErrorMessage"]?.ToString()}";
            }


            return Page();
        }
    }
}