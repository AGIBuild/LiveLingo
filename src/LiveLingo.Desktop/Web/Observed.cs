using Microsoft.Extensions.Logging;

namespace LiveLingo.Desktop.Web;

internal static class Observed
{
    /// <summary>Runs work the page does not wait for. A failure is logged, never lost.</summary>
    public static void Run(Func<Task> action, ILogger logger, string what) => _ = RunAsync(action, logger, what);

    private static async Task RunAsync(Func<Task> action, ILogger logger, string what)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The type only: an exception message can carry the user's text.
            logger.LogError("{What} failed: {Exception}", what, ex.GetType().Name);
        }
    }
}
