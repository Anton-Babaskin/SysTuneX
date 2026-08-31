## Download

`SysTuneX.exe` below is a self-contained single file — no .NET runtime to install. It asks for
administrator rights on launch because the registry keys it writes live under `HKEY_LOCAL_MACHINE`,
changing a service's start type goes through the service control manager, and the frame counter
needs an Event Tracing session. All three refuse a standard token.

## Verifying it

You are about to run an unsigned executable as administrator. That deserves a check, and there is a
real one available.

```powershell
Get-FileHash .\SysTuneX.exe -Algorithm SHA256
```

That number should match `SHA256SUMS.txt` beside the download — **and** the SHA-256 printed by the
**Checksum** step of the [Actions run](https://github.com/Anton-Babaskin/SysTuneX/actions) that
built this release, in a public log nobody can edit afterwards.

If all three agree, this is the file that GitHub's runner built from the tagged commit. Nobody,
including whoever controls this release page, can swap the binary without the numbers disagreeing.
That is a stronger guarantee than a code signature, which proves only who shipped a file and
nothing about what it does.

**SmartScreen will warn**, because the executable is not code-signed — a certificate that avoids
that costs several hundred dollars a year and requires a registered company. The warning is about
how many people have run this file, not about what it does. Choose **More info → Run anyway**, or
check the hash first, or put the file through [VirusTotal](https://www.virustotal.com/).

Full detail, including everything the app writes and how each change is undone, is in the
repository's **Trust and safety** section.

---

## Known limitations

* Not code-signed, so SmartScreen will warn.
* The hosts-file block can be refused by Microsoft Defender tamper protection. The app reports that
  rather than failing silently.
* Restore points need System Protection switched on for the system drive; if it is off, the app says
  so instead of pretending a restore point was created.
* Ultimate Performance is unavailable on some Windows editions. SysTuneX falls back to High
  Performance and reports which one it used.
* Cleanup deletes files and is the one action that cannot be undone from the change journal. The app
  says so before doing it.
