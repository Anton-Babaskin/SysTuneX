using SysTuneX.Core.Models;

namespace SysTuneX.Core.Abstractions;

/// <summary>
/// Work that has to happen after a registry write for the change to take effect on a running
/// desktop - telling Windows to re-read what we just wrote.
///
/// One per flag, resolved as a set. This was a chain of <c>if (tweak.PostApply.HasFlag(X))</c>
/// inside TweakEngine, each branch calling a static P/Invoke helper: the engine knew about every
/// kind of refresh there is, and none of it could be reached from a test because reaching it meant
/// calling SystemParametersInfo on a live desktop.
/// </summary>
public interface IPostApplyEffect
{
    /// <summary>The flag this effect answers to. One flag each; a tweak may ask for several.</summary>
    PostApplyAction Flag { get; }

    /// <summary>
    /// Runs the refresh.
    /// </summary>
    /// <param name="applying">
    /// True when the tweak is being applied, false when reverted. Most of these push a value that
    /// is the opposite of the tweak's own sense - "mouse acceleration off" applies by enabling the
    /// tweak and therefore by disabling acceleration.
    /// </param>
    void Run(bool applying);
}
