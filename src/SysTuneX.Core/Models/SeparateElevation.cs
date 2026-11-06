namespace SysTuneX.Core.Models;

/// <summary>
/// SysTuneX was elevated into a different account from the person signed in at the desktop.
///
/// Two things do that. Over-the-shoulder elevation: a standard user types an administrator's
/// password into the UAC prompt, and the process runs as that administrator. And Administrator
/// protection, which Windows 11 24H2 to 26H2 can turn on from Windows Security and the 26H2
/// security baseline turns on by default: every elevation runs as a hidden, system-managed account
/// with a profile of its own. Either way HKEY_CURRENT_USER in this process is the other account's
/// hive, so a per-user setting written there is reported as applied and never reaches the person
/// using the machine.
/// </summary>
/// <param name="ElevatedAccount">The account the process runs as, DOMAIN\name.</param>
/// <param name="DesktopAccount">The account signed in to this desktop session, DOMAIN\name.</param>
/// <param name="DesktopSid">
/// The desktop account's SID: the name of its hive under HKEY_USERS, loaded while it is signed in.
/// </param>
public sealed record SeparateElevation(string ElevatedAccount, string DesktopAccount, string DesktopSid);
