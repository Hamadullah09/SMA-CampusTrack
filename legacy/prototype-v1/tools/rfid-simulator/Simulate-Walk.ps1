<#
.SYNOPSIS
  Simulates a student walking through an RFID reader so you can test the
  antenna-sequence attendance logic without physical hardware.

.EXAMPLE
  # entry through the 3-antenna gate (antennas 1 -> 2 -> 3)
  .\Simulate-Walk.ps1 -ReaderCode GATE-01 -Epc E20034120001 -Direction Entry -Antennas 3

  # exit from a 2-antenna classroom (antennas 2 -> 1)
  .\Simulate-Walk.ps1 -ReaderCode RDR-2 -Epc E20034120001 -Direction Exit -Antennas 2
#>
param(
    [string]$ApiUrl     = "http://localhost:5000",
    [string]$ApiKey     = "CHANGE-ME-reader-shared-key",
    [Parameter(Mandatory)][string]$ReaderCode,
    [Parameter(Mandatory)][string]$Epc,
    [ValidateSet("Entry","Exit")][string]$Direction = "Entry",
    [ValidateSet(2,3)][int]$Antennas = 3
)

$sequence = if ($Direction -eq "Entry") { 1..$Antennas } else { $Antennas..1 }

$reads = @()
$t = Get-Date
foreach ($antenna in $sequence) {
    # a real UHF reader reports the same tag several times per antenna zone
    foreach ($i in 1..3) {
        $reads += @{
            readerCode = $ReaderCode
            antennaNo  = $antenna
            epc        = $Epc
            readTime   = $t.ToUniversalTime().ToString("o")
        }
        $t = $t.AddMilliseconds(180)
    }
}

$body = @{ reads = $reads } | ConvertTo-Json -Depth 4
$response = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/rfid/reads" `
    -ContentType "application/json" -Body $body `
    -Headers @{ "X-Reader-ApiKey" = $ApiKey }

Write-Host "Sent $($reads.Count) reads ($Direction via $ReaderCode)."
Write-Host "Server accepted: $($response.accepted)/$($response.received)"
Write-Host "The sweep service resolves the event ~4s after the last read."
