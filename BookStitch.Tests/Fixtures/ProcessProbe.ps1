param([Parameter(Mandatory = $true)][string]$PipeName)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$pipe = New-Object System.IO.Pipes.NamedPipeServerStream($PipeName, [System.IO.Pipes.PipeDirection]::In)
$reader = $null
try {
    $pipe.WaitForConnection()
    $reader = New-Object System.IO.StreamReader($pipe)
    while ($null -ne ($line = $reader.ReadLine())) {
        $parts = $line.Split('|', 3)
        if ($parts[0] -eq 'exit') { exit ([int]$parts[1]) }
        if ($parts[0] -eq 'out-exit') {
            [Console]::Out.WriteLine($parts[2])
            [Console]::Out.Flush()
            exit ([int]$parts[1])
        }
        $count = [int]$parts[1]
        $text = $parts[2]
        for ($i = 0; $i -lt $count; $i++) {
            switch ($parts[0]) {
                'out' { [Console]::Out.WriteLine($text) }
                'err' { [Console]::Error.WriteLine($text) }
                'both' { [Console]::Out.WriteLine($text); [Console]::Error.WriteLine($text) }
                'raw' { [Console]::Out.Write($text); [Console]::Error.Write($text) }
                default { throw "Unknown probe command: $($parts[0])" }
            }
        }
        [Console]::Out.Flush()
        [Console]::Error.Flush()
    }
}
finally {
    if ($null -ne $reader) { $reader.Dispose() }
    $pipe.Dispose()
}
