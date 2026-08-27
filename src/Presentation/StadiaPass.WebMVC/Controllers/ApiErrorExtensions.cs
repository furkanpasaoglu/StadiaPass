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
            // A key the form never bound has no field to sit next to, and a ModelOnly summary ignores keyed
            // errors - so it would vanish and the page would look like nothing happened. Those are reported
            // at the top instead.
            var key = controller.ModelState.ContainsKey(property) ? property : string.Empty;

            foreach (var message in messages)
            {
                controller.ModelState.AddModelError(key, message);
            }
        }
    }
}
