using SysTuneX.Core.Abstractions;
using SysTuneX.Core.Models;

namespace SysTuneX.Core.Tests.Fakes;

/// <summary>The process runs as the desktop's own account unless a test says otherwise.</summary>
public sealed class FakeDesktopSession : IDesktopSession
{
    public SeparateElevation? SeparateElevation { get; set; }
}
