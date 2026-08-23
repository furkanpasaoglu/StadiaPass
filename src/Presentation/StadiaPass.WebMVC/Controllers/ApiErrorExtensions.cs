using Microsoft.AspNetCore.Mvc;

namespace StadiaPass.WebMVC.Controllers;

internal static class ApiErrorExtensions
{
    /// <summary>
    /// Copies the failure the API reported into ModelState so the form renders it next to the offending
    /// field, falling back to a summary message when the API did not send field level detail.
    /// </summary>
    public static void ApplyApiErrors(
        this Controller controller,
        string? error,
        IReadOnlyDictionary<string, string[]>? validationErrors)
    {
        if (validationErrors is null)
        {
            controller.ModelState.AddModelError(string.Empty, error ?? "The request could not be completed.");

            return;
        }

        foreach (var (property, messages) in validationErrors)
        {
            foreach (var message in messages)
            {
                controller.ModelState.AddModelError(property, message);
            }
        }
    }
}
