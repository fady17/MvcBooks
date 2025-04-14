// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#nullable disable // Keep default nullable context for scaffolded Identity files

using System;
using System.Collections.Generic; // For personal data dictionary
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq; // For personal data select
using System.Text; // For personal data download
using System.Text.Encodings.Web;
using System.Text.Json; // For personal data download
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services; // Added for IEmailSender
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging; // Added for logging
using MvcBooks.Models;

namespace MvcBooks.Areas.Identity.Pages.Account.Manage
{
    public class IndexModel : PageModel
    {
        private readonly UserManager<User> _userManager;
        private readonly SignInManager<User> _signInManager;
        private readonly IWebHostEnvironment _hostEnvironment;
        private readonly IEmailSender _emailSender; // Added
        private readonly ILogger<IndexModel> _logger; // Added

        private readonly string _profilePictureFolder = Path.Combine("images", "profiles");

        public IndexModel(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IWebHostEnvironment hostEnvironment,
            IEmailSender emailSender, // Added
            ILogger<IndexModel> logger) // Added
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _hostEnvironment = hostEnvironment;
            _emailSender = emailSender; // Added
            _logger = logger; // Added
        }

        // --- Properties for Display ---
        public string Username { get; set; }
        public string Email { get; set; } // Added for display
        public bool IsEmailConfirmed { get; set; } // Added for display

        [TempData]
        public string StatusMessage { get; set; }

        // --- Bound Input Models for Forms ---
        [BindProperty]
        public ProfileInputModel ProfileInput { get; set; }

        [BindProperty]
        public ChangeEmailInputModel ChangeEmailInput { get; set; } // Added

        [BindProperty]
        public ChangePasswordInputModel ChangePasswordInput { get; set; } // Added


        // --- Input Model Definitions ---
        public class ProfileInputModel
        {
            [Phone]
            [Display(Name = "Phone number")]
            public string PhoneNumber { get; set; }

            [Display(Name = "First Name")]
            [StringLength(100)]
            public string FirstName { get; set; }

            [Display(Name = "Last Name")]
            [StringLength(100)]
            public string LastName { get; set; }

            [Display(Name = "Current Profile Picture")]
            public string ExistingProfilePictureUrl { get; set; }

            [Display(Name = "Upload New Profile Picture")]
            public IFormFile ProfileImageUpload { get; set; }
        }

        public class ChangeEmailInputModel // Copied & adapted from Email.cshtml.cs
        {
            [Required]
            [EmailAddress]
            [Display(Name = "New email")]
            public string NewEmail { get; set; }
        }

        public class ChangePasswordInputModel // Copied & adapted from ChangePassword.cshtml.cs
        {
            [Required]
            [DataType(DataType.Password)]
            [Display(Name = "Current password")]
            public string OldPassword { get; set; }

            [Required]
            [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
            [DataType(DataType.Password)]
            [Display(Name = "New password")]
            public string NewPassword { get; set; }

            [DataType(DataType.Password)]
            [Display(Name = "Confirm new password")]
            [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
            public string ConfirmPassword { get; set; }
        }


        // --- Helper to Load User Data ---
        private async Task LoadUserDataAsync(User user)
        {
            var userName = await _userManager.GetUserNameAsync(user);
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            var email = await _userManager.GetEmailAsync(user);

            Username = userName;
            Email = email; // Populate Email property
            IsEmailConfirmed = await _userManager.IsEmailConfirmedAsync(user); // Populate confirmation status

            ProfileInput = new ProfileInputModel
            {
                PhoneNumber = phoneNumber,
                FirstName = user.FirstName,
                LastName = user.LastName,
                ExistingProfilePictureUrl = user.ProfilePictureUrl
            };

            // Initialize other input models (optional, prevents null refs if accessed before POST)
            ChangeEmailInput ??= new ChangeEmailInputModel { NewEmail = email }; // Pre-fill ChangeEmail modal?
            ChangePasswordInput ??= new ChangePasswordInputModel();
        }

        // --- HTTP Handlers ---

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");
            }

            await LoadUserDataAsync(user);
            return Page();
        }

        // --- Handler for Basic Profile Update ---
        public async Task<IActionResult> OnPostUpdateProfileAsync() // Renamed from OnPostAsync
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");

            // Use ProfileInput model state
            if (!ModelState.IsValid) // Check validation specific to ProfileInput if needed
            {
                 // If only ProfileInput validation fails, reload all data
                 await LoadUserDataAsync(user);
                 return Page();
            }

            // Update Phone
            var phoneNumber = await _userManager.GetPhoneNumberAsync(user);
            if (ProfileInput.PhoneNumber != phoneNumber)
            {
                var setPhoneResult = await _userManager.SetPhoneNumberAsync(user, ProfileInput.PhoneNumber);
                if (!setPhoneResult.Succeeded) { StatusMessage = "Error: Unexpected error when trying to set phone number."; return RedirectToPage(); }
            }

            // Update Name & Picture
            bool profileUpdated = false;
            if (ProfileInput.FirstName != user.FirstName) { user.FirstName = ProfileInput.FirstName; profileUpdated = true; }
            if (ProfileInput.LastName != user.LastName) { user.LastName = ProfileInput.LastName; profileUpdated = true; }

            string oldProfilePicturePath = user.ProfilePictureUrl;
            string uniqueFileName = null;
            if (ProfileInput.ProfileImageUpload != null && ProfileInput.ProfileImageUpload.Length > 0)
            {
                // ... (File Validation Logic as before - Size, Type) ...
                if (ProfileInput.ProfileImageUpload.Length > 2 * 1024 * 1024) { ModelState.AddModelError("ProfileInput.ProfileImageUpload", "Max 2MB"); await LoadUserDataAsync(user); return Page(); }
                string[] allowedExtensions = { ".jpg", ".jpeg", ".png", ".gif" };
                string fileExtension = Path.GetExtension(ProfileInput.ProfileImageUpload.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension)) { ModelState.AddModelError("ProfileInput.ProfileImageUpload", "Invalid file type"); await LoadUserDataAsync(user); return Page(); }

                string uploadsFolder = Path.Combine(_hostEnvironment.WebRootPath, _profilePictureFolder);
                Directory.CreateDirectory(uploadsFolder);
                string baseFileName = $"{user.Id}_{Guid.NewGuid()}";
                uniqueFileName = Path.Combine(_profilePictureFolder, baseFileName + fileExtension);
                string filePath = Path.Combine(uploadsFolder, baseFileName + fileExtension);

                try
                {
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await ProfileInput.ProfileImageUpload.CopyToAsync(fileStream);
                    }
                    user.ProfilePictureUrl = "/" + uniqueFileName.Replace(Path.DirectorySeparatorChar, '/');
                    profileUpdated = true;
                    // Delete old picture (if different)
                     if (!string.IsNullOrEmpty(oldProfilePicturePath) && oldProfilePicturePath != user.ProfilePictureUrl)
                     {
                        string oldAbsoluteFilePath = Path.Combine(_hostEnvironment.WebRootPath, oldProfilePicturePath.TrimStart('/'));
                        if (System.IO.File.Exists(oldAbsoluteFilePath)) { try { System.IO.File.Delete(oldAbsoluteFilePath); } catch (IOException ex) { _logger.LogWarning(ex, "Could not delete old profile picture {Path}", oldAbsoluteFilePath); } }
                     }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving profile picture for user {UserId}", user.Id);
                    ModelState.AddModelError("", "An error occurred saving the profile picture.");
                    await LoadUserDataAsync(user); return Page();
                }
            }

            // Save User Changes
            if (profileUpdated)
            {
                var updateResult = await _userManager.UpdateAsync(user);
                if (!updateResult.Succeeded) { StatusMessage = "Error: Unexpected error updating profile."; return RedirectToPage(); }
            }

            await _signInManager.RefreshSignInAsync(user);
            StatusMessage = "Your profile has been updated";
            return RedirectToPage();
        }

        // --- Handler for Changing Password ---
        public async Task<IActionResult> OnPostChangePasswordAsync() // New handler
        {
            // Check validation specific to ChangePasswordInput model state
             if (!ModelState.IsValid) // Note: This checks ALL bound models. Need refinement if used with modals.
            {
                // Ideally, if validation fails, we return specific errors for the modal,
                // but for simplicity now, just return Page(). User has to reopen modal.
                await LoadUserDataAsync(await _userManager.GetUserAsync(User)); // Reload data
                // Add specific model state errors to TempData or a ViewBag maybe?
                StatusMessage = "Error: Invalid password change attempt."; // Generic message
                return Page();
            }

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");

            var changePasswordResult = await _userManager.ChangePasswordAsync(user, ChangePasswordInput.OldPassword, ChangePasswordInput.NewPassword);
            if (!changePasswordResult.Succeeded)
            {
                foreach (var error in changePasswordResult.Errors)
                {
                    // Add errors to ModelState for general display, though modal won't show them directly now
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                StatusMessage = "Error: Could not change password. " + string.Join(" ", changePasswordResult.Errors.Select(e => e.Description)); // Show errors in status
                await LoadUserDataAsync(user); // Reload data
                return Page(); // Or redirect to prevent form resubmission issues
                 // return RedirectToPage(); // This would clear ModelState errors
            }

            await _signInManager.RefreshSignInAsync(user);
            _logger.LogInformation("User changed their password successfully.");
            StatusMessage = "Your password has been changed.";

            return RedirectToPage();
        }

        // --- Handler for Sending Verification Email ---
        public async Task<IActionResult> OnPostSendVerificationEmailAsync() // New handler
        {
             // No separate input model needed here, just check model state overall if necessary
            // if (!ModelState.IsValid) { return Page(); } // Less likely needed here

            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");

            var userId = await _userManager.GetUserIdAsync(user);
            var email = await _userManager.GetEmailAsync(user);
            var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
             // Generate callback URL - ensure routing is correct
             var callbackUrl = Url.Page(
                 "/Account/ConfirmEmail", // Path to Identity ConfirmEmail page
                 pageHandler: null,
                 values: new { area = "Identity", userId = userId, code = code },
                 protocol: Request.Scheme);

             await _emailSender.SendEmailAsync(
                 email,
                 "Confirm your email",
                 $"Please confirm your account by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

            StatusMessage = "Verification email sent. Please check your email.";
            return RedirectToPage();
        }


        // --- Handler for Changing Email ---
        public async Task<IActionResult> OnPostChangeEmailAsync() // New handler
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");

            // Check validation specific to ChangeEmailInput model state
            if (!ModelState.IsValid) // Again, this might be too broad.
            {
                StatusMessage = "Error: Invalid email change attempt.";
                await LoadUserDataAsync(user); return Page();
            }

            var email = await _userManager.GetEmailAsync(user);
            if (ChangeEmailInput.NewEmail != email)
            {
                var userId = await _userManager.GetUserIdAsync(user);
                var code = await _userManager.GenerateChangeEmailTokenAsync(user, ChangeEmailInput.NewEmail);
                var callbackUrl = Url.Page(
                    "/Account/ConfirmEmailChange", // Path to Identity ConfirmEmailChange page
                    pageHandler: null,
                    values: new { area = "Identity", userId = userId, email = ChangeEmailInput.NewEmail, code = code },
                    protocol: Request.Scheme);
                await _emailSender.SendEmailAsync(
                    ChangeEmailInput.NewEmail,
                    "Confirm your email change",
                    $"Please confirm your account email change by <a href='{HtmlEncoder.Default.Encode(callbackUrl)}'>clicking here</a>.");

                StatusMessage = "Confirmation link to change email sent. Please check your new email address.";
                return RedirectToPage();
            }

            StatusMessage = "Error: Your email is unchanged."; // Or just "Your email is unchanged."
            return RedirectToPage();
        }

        // --- Handler for Downloading Personal Data ---
         public async Task<IActionResult> OnPostDownloadPersonalDataAsync() // New handler
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound($"Unable to load user with ID '{_userManager.GetUserId(User)}'.");

            _logger.LogInformation("User with ID '{UserId}' asked for their personal data.", _userManager.GetUserId(User));

            // Only include personal data sensitive enough to be considered personal data
            var personalData = new Dictionary<string, string>();
            var personalDataProps = typeof(User).GetProperties().Where(
                            prop => Attribute.IsDefined(prop, typeof(PersonalDataAttribute)));
            foreach (var p in personalDataProps)
            {
                personalData.Add(p.Name, p.GetValue(user)?.ToString() ?? "null");
            }

            var logins = await _userManager.GetLoginsAsync(user);
            foreach (var l in logins)
            {
                personalData.Add($"{l.LoginProvider} external login provider key", l.ProviderKey);
            }

            personalData.Add($"Authenticator Key", await _userManager.GetAuthenticatorKeyAsync(user));

            Response.Headers.TryAdd("Content-Disposition", "attachment; filename=PersonalData.json");
            return new FileContentResult(JsonSerializer.SerializeToUtf8Bytes(personalData), "application/json");
        }


        // --- Placeholder Handler for Deleting Account ---
        // Actual deletion is complex and often delegated to a separate confirmation page (DeletePersonalData.cshtml)
        // This handler might just redirect there or set up data for the confirmation modal.
        public IActionResult OnPostDeleteAccountAsync()
        {
             // This would typically redirect to a separate confirmation page like DeletePersonalData
             // return RedirectToPage("DeletePersonalData");

             // Or, if using a modal, set a flag/message and return Page()
             StatusMessage = "Account deletion requires confirmation (modal not fully implemented).";
             return RedirectToPage();
        }
    }
}