using SysTuneX.App.Diagnostics;

namespace SysTuneX.App.Tests;

/// <summary>
/// Carries a UI-thread failure back to the test thread with the whole inner chain rendered,
/// because the outer message of a XAML failure names neither the file nor the value.
/// </summary>
public sealed class WpfHostException(Exception inner)
    : Exception(ExceptionReport.Describe(inner), inner);
