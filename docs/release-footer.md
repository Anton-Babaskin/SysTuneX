## Download

`SysTuneX.exe` below is a self-contained single file. No .NET runtime install needed. It requests
administrator rights on launch, because every change it makes needs a full administrator token.

Verify the download against `SHA256SUMS.txt` if you like:

```powershell
Get-FileHash .\SysTuneX.exe -Algorithm SHA256
```

Windows SmartScreen will warn about an unsigned executable — the binary is not code-signed.
Choose **More info → Run anyway** if you are happy with that.

---

---

## Known limitations

* Not code-signed, so SmartScreen will warn.
* The hosts-file block can be refused by Microsoft Defender tamper protection. The app reports that
  rather than failing silently.
* Restore points need System Protection switched on for the system drive; if it is off, the app says
  so instead of pretending a restore point was created.
* Ultimate Performance is unavailable on some Windows editions. SysTuneX falls back to High
  Performance and reports which one it used.
