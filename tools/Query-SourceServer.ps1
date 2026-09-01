[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Address,

    [int]$Port = 27015,

    [int]$TimeoutMilliseconds = 5000
)

$ErrorActionPreference = 'Stop'

function Read-NullTerminatedString {
    param([Parameter(Mandatory)][System.IO.BinaryReader]$Reader)

    $bytes = [System.Collections.Generic.List[byte]]::new()
    while ($true) {
        $value = $Reader.ReadByte()
        if ($value -eq 0) {
            break
        }
        $bytes.Add($value)
    }
    [System.Text.Encoding]::UTF8.GetString($bytes.ToArray())
}

function Receive-Response {
    param([Parameter(Mandatory)][System.Net.Sockets.UdpClient]$Client)

    $remote = [System.Net.IPEndPoint]::new([System.Net.IPAddress]::Any, 0)
    $Client.Receive([ref]$remote)
}

$payload = [System.Text.Encoding]::ASCII.GetBytes("Source Engine Query`0")
$query = [byte[]](@(0xFF, 0xFF, 0xFF, 0xFF, 0x54) + $payload)
$client = [System.Net.Sockets.UdpClient]::new()
try {
    $client.Client.ReceiveTimeout = $TimeoutMilliseconds
    $client.Client.SendTimeout = $TimeoutMilliseconds
    $client.Connect($Address, $Port)

    [void]$client.Send($query, $query.Length)
    $response = Receive-Response -Client $client

    if ($response.Length -ge 9 -and $response[4] -eq 0x41) {
        $challengeQuery = [byte[]]::new($query.Length + 4)
        [Array]::Copy($query, $challengeQuery, $query.Length)
        [Array]::Copy($response, 5, $challengeQuery, $query.Length, 4)
        [void]$client.Send($challengeQuery, $challengeQuery.Length)
        $response = Receive-Response -Client $client
    }

    $stream = [System.IO.MemoryStream]::new($response, $false)
    $reader = [System.IO.BinaryReader]::new($stream, [System.Text.Encoding]::UTF8, $false)
    if ($reader.ReadInt32() -ne -1) {
        throw 'Split Source query responses are not supported.'
    }
    $responseType = $reader.ReadByte()
    if ($responseType -ne 0x49) {
        throw ('Unexpected A2S_INFO response type: 0x{0:X2}' -f $responseType)
    }

    $protocol = $reader.ReadByte()
    $name = Read-NullTerminatedString -Reader $reader
    $map = Read-NullTerminatedString -Reader $reader
    $folder = Read-NullTerminatedString -Reader $reader
    $game = Read-NullTerminatedString -Reader $reader
    $appId = $reader.ReadUInt16()
    $players = $reader.ReadByte()
    $maxPlayers = $reader.ReadByte()
    $bots = $reader.ReadByte()
    $serverType = [char]$reader.ReadByte()
    $environment = [char]$reader.ReadByte()
    $visibility = $reader.ReadByte()
    $vac = $reader.ReadByte()
    $version = Read-NullTerminatedString -Reader $reader

    [pscustomobject]@{
        Address     = $Address
        Port        = $Port
        Protocol    = $protocol
        Name        = $name
        Map         = $map
        Folder      = $folder
        Game        = $game
        AppId       = $appId
        Players     = $players
        MaxPlayers  = $maxPlayers
        Bots        = $bots
        ServerType  = $serverType
        Environment = $environment
        Password    = $visibility -ne 0
        VacSecure   = $vac -ne 0
        Version     = $version
    }
}
finally {
    $client.Dispose()
}
