$ErrorActionPreference = 'Stop'
# Pinned official PuTTY release; never ship the legacy vendored executables.
# Hashes: https://the.earth.li/~sgtatham/putty/0.85/sha256sums
$hashes = @{
  'putty.exe' = '4c70267ca03a00ea761ec358498b990a7221decb36eace9b7dbe6a751be0fb3b'
  'pageant.exe' = '16818a55ab56ce5dc080da7d3655c8dca6539384801ec96c30744839a1ce6d96'
  'puttygen.exe' = '0912f3ab31c1cdc60e749406e1dd8e0fc4867dfa4d85062ce10ce201d7f77511'
}
$directory = 'upstream/Terminals/Resources/Libraries/Putty'
New-Item -ItemType Directory -Force $directory | Out-Null
foreach ($name in $hashes.Keys) {
  $destination = Join-Path $directory $name
  Invoke-WebRequest "https://the.earth.li/~sgtatham/putty/0.85/w32/$name" -OutFile $destination
  if ((Get-FileHash $destination -Algorithm SHA256).Hash -ne $hashes[$name]) { throw "PuTTY checksum mismatch: $name" }
  $signature = Get-AuthenticodeSignature $destination
  if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Simon Tatham') { throw "PuTTY signature rejected: $name" }
}
Copy-Item 'docs/licenses/PuTTY-0.85.txt' (Join-Path $directory 'LICENCE-PuTTY.txt')
