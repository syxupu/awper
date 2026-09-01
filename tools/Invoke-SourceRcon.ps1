[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Address,

    [int]$Port = 27015,

    [Parameter(Mandatory)]
    [string]$Password,

    [Parameter(Mandatory)]
    [string]$Command,

    [int]$TimeoutMilliseconds = 5000
)

$ErrorActionPreference = 'Stop'

function Write-RconPacket {
    param(
        [Parameter(Mandatory)][System.IO.Stream]$Stream,
        [Parameter(Mandatory)][int]$Id,
        [Parameter(Mandatory)][int]$Type,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Body
    )

    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
    $packetSize = 4 + 4 + $bodyBytes.Length + 2
    $writer = [System.IO.BinaryWriter]::new($Stream, [System.Text.Encoding]::UTF8, $true)
    $writer.Write($packetSize)
    $writer.Write($Id)
    $writer.Write($Type)
    $writer.Write($bodyBytes)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Flush()
}

function Read-RconPacket {
    param([Parameter(Mandatory)][System.IO.Stream]$Stream)

    $reader = [System.IO.BinaryReader]::new($Stream, [System.Text.Encoding]::UTF8, $true)
    $size = $reader.ReadInt32()
    if ($size -lt 10 -or $size -gt 4MB) {
        throw "Invalid RCON packet size: $size"
    }
    $id = $reader.ReadInt32()
    $type = $reader.ReadInt32()
    $payloadLength = $size - 10
    $payload = [System.Text.Encoding]::UTF8.GetString($reader.ReadBytes($payloadLength))
    [void]$reader.ReadByte()
    [void]$reader.ReadByte()
    [pscustomobject]@{ Id = $id; Type = $type; Body = $payload }
}

$client = [System.Net.Sockets.TcpClient]::new()
try {
    $connectTask = $client.ConnectAsync($Address, $Port)
    if (-not $connectTask.Wait($TimeoutMilliseconds)) {
        throw "Timed out connecting to RCON at ${Address}:$Port"
    }
    $stream = $client.GetStream()
    $stream.ReadTimeout = $TimeoutMilliseconds
    $stream.WriteTimeout = $TimeoutMilliseconds

    Write-RconPacket -Stream $stream -Id 1 -Type 3 -Body $Password
    $authenticated = $false
    for ($i = 0; $i -lt 3 -and -not $authenticated; $i++) {
        $response = Read-RconPacket -Stream $stream
        Write-Verbose "Auth packet: id=$($response.Id), type=$($response.Type), bytes=$([System.Text.Encoding]::UTF8.GetByteCount($response.Body))"
        if ($response.Type -eq 2) {
            if ($response.Id -eq -1) { throw 'RCON authentication failed.' }
            $authenticated = $response.Id -eq 1
        }
    }
    if (-not $authenticated) { throw 'RCON authentication response was not received.' }

    Write-RconPacket -Stream $stream -Id 2 -Type 2 -Body $Command
    Write-RconPacket -Stream $stream -Id 3 -Type 2 -Body ''
    $lines = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt 16; $i++) {
        try {
            $response = Read-RconPacket -Stream $stream
        }
        catch {
            Write-Verbose "RCON response collection ended: $($_.Exception.Message)"
            break
        }
        Write-Verbose "Command packet: id=$($response.Id), type=$($response.Type), bytes=$([System.Text.Encoding]::UTF8.GetByteCount($response.Body))"
        # CS2 currently tags command output with the terminator request id (3),
        # while Source RCON implementations commonly echo the command id (2).
        # Accept both so diagnostics are not silently discarded.
        if ($response.Id -in 2, 3 -and $response.Body) { $lines.Add($response.Body) }
    }
    $lines -join ''
}
finally {
    $client.Dispose()
}
