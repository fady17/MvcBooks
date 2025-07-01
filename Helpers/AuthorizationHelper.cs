using System.Security.Claims;

namespace MvcBooks.Helpers // Or your preferred namespace
{
    public static class AuthorizationHelper
    {
        public const string AdminRoleName = "Admin"; // Centralize role name

        /// <summary>
        /// Checks if the user is authorized to access a resource.
        /// Authorization is granted if the user owns the resource OR is an Admin.
        /// </summary>
        /// <param name="user">The ClaimsPrincipal representing the current user.</param>
        /// <param name="resourceOwnerId">The ID of the user who owns the resource.</param>
        /// <returns>True if authorized, false otherwise.</returns>
        public static bool IsUserAuthorized(ClaimsPrincipal user, string? resourceOwnerId)
        {
            if (user == null)
            {
                return false; // Cannot authorize null user
            }

            var currentUserId = user.FindFirstValue(ClaimTypes.NameIdentifier);

            // Check if the user owns the resource
            bool isOwner = resourceOwnerId != null && resourceOwnerId == currentUserId;

            // Check if the user is in the Admin role
            bool isAdmin = user.IsInRole(AdminRoleName);

            return isOwner || isAdmin;
        }
    }
}