namespace LiveLingo.Desktop.ViewModels.Settings;

/// <summary>
/// Owns the "currently loading from disk" flag used by the Settings ViewModel
/// to suppress IsDirty side-effects while the working copy is being rebuilt.
/// </summary>
internal interface ISettingsDirtyGuard
{
    /// <summary>True while a <see cref="RunLoading"/> action is on the stack.</summary>
    bool IsLoading { get; }

    /// <summary>
    /// Runs <paramref name="action"/> with <see cref="IsLoading"/> set to true.
    /// Reverts the flag in a <c>finally</c> block so an exception still leaves
    /// the guard in a usable state.
    /// </summary>
    void RunLoading(Action action);
}
