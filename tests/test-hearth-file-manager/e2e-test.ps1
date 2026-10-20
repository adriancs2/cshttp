# End-to-end drop-in test: HearthFileManager running on cshttp (no IIS, no System.Web).
# Usage: powershell -ExecutionPolicy Bypass -File e2e-test.ps1 [-Port 18080]
param([int]$Port = 18080)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe  = Join-Path $here 'HearthFileManager\bin\Debug\net48\HearthFileManager.exe'
$base = "http://localhost:$Port"
$tmp  = Join-Path $env:TEMP ("hearth-e2e-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $tmp | Out-Null
$jar = Join-Path $tmp 'cookies.txt'
$script:pass = 0; $script:fail = 0

function Http([string[]]$a) {
    $h = Join-Path $tmp 'h.txt'; $b = Join-Path $tmp 'b.bin'
    Remove-Item $h, $b -ErrorAction SilentlyContinue
    [IO.File]::WriteAllBytes($b, (New-Object byte[] 0))
    $code = & curl.exe -s -o $b -D $h -w '%{http_code}' -b $jar -c $jar @a
    $bytes = [IO.File]::ReadAllBytes($b)
    [pscustomobject]@{ Status = [int]$code; Head = [IO.File]::ReadAllText($h); Bytes = $bytes; Text = [Text.Encoding]::UTF8.GetString($bytes) }
}
function Header($r, $name) { $m = [regex]::Match($r.Head, "(?im)^$([regex]::Escape($name)):\s*(.*?)\s*$"); if ($m.Success) { $m.Groups[1].Value } else { $null } }
function Json($r) { try { $r.Text | ConvertFrom-Json } catch { $null } }
function Check([string]$name, [bool]$ok, [string]$detail = '') {
    if ($ok) { $script:pass++; Write-Host "  ok    $name" }
    else { $script:fail++; Write-Host "  FAIL  $name  $detail" -ForegroundColor Red }
}
function Api([string]$endpoint, [string]$action, [hashtable]$fields) {
    $a = @('-H', 'X-Hearth: 1', '-F', "action=$action")
    foreach ($k in $fields.Keys) { $a += @('--form-string', (Q "$k=$($fields[$k])")) }
    Http ($a + "$base$endpoint")
}
# Windows PowerShell 5 drops embedded double quotes when calling native programs; escape them.
function Q([string]$s) { $s -replace '"', '\"' }
function Sha([byte[]]$b) { [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($b)) }

$log = Join-Path $tmp 'server.log'
$proc = Start-Process -FilePath $exe -ArgumentList "--port $Port" -PassThru -WindowStyle Hidden -RedirectStandardOutput $log -RedirectStandardError "$log.err"
try {
    $up = $false
    for ($i = 0; $i -lt 50 -and -not $up; $i++) { Start-Sleep -Milliseconds 200; try { $c = New-Object Net.Sockets.TcpClient('127.0.0.1', $Port); $c.Close(); $up = $true } catch { } }
    if (-not $up) { throw "server did not start: $(Get-Content $log -Raw) $(Get-Content "$log.err" -Raw)" }
    Write-Host "HearthFileManager on cshttp is up (pid $($proc.Id))"

    Write-Host "-- routing and pages"
    $r = Http @("$base/");                  Check 'GET / redirects to /files' ($r.Status -eq 302 -and (Header $r 'Location') -eq '/files') "$($r.Status)"
    $r = Http @("$base/files");             Check 'not signed in: /files redirects to /login' ($r.Status -eq 302 -and (Header $r 'Location') -eq '/login') "$($r.Status)"
    $r = Http @("$base/login");             Check 'GET /login renders the page' ($r.Status -eq 200 -and $r.Text -match 'login-form') "$($r.Status)"
    Check 'security headers from old web.config' ((Header $r 'X-Content-Type-Options') -eq 'nosniff' -and (Header $r 'Referrer-Policy') -eq 'same-origin')
    $cssLink = [regex]::Match($r.Text, "/css/site\.css\?v=\d+").Value
    Check 'StaticAsset version stamp (HostingEnvironment.MapPath)' ($cssLink -ne '' -and $cssLink -notmatch 'v=0$') $cssLink
    $r = Http @("$base/no-such-page");      Check 'unknown URL gives 404' ($r.Status -eq 404) "$($r.Status)"

    Write-Host "-- static files (served by IIS before)"
    $r = Http @("$base$cssLink");           Check 'versioned css is immutable' ($r.Status -eq 200 -and (Header $r 'Cache-Control') -match 'immutable' -and (Header $r 'Content-Type') -match 'text/css') "$(Header $r 'Cache-Control')"
    $r = Http @("$base/js/files.js");       Check 'js served' ($r.Status -eq 200 -and (Header $r 'Content-Type') -match 'javascript')
    $etag = Header $r 'ETag'
    $r = Http @('-H', (Q "If-None-Match: $etag"), "$base/js/files.js"); Check 'js revalidation gives 304' ($r.Status -eq 304) "$($r.Status)"
    $r = Http @("$base/assets/fontawesome/webfonts/fa-solid-900.woff2"); Check 'woff2 font with right MIME' ($r.Status -eq 200 -and (Header $r 'Content-Type') -eq 'font/woff2' -and $r.Bytes.Length -eq 114740) "$(Header $r 'Content-Type') $($r.Bytes.Length)"
    $r = Http @("$base/App_Data/config.json"); Check 'App_Data is not reachable' ($r.Status -eq 404 -and $r.Text -notmatch 'PasswordHash') "$($r.Status)"
    $r = Http @('--path-as-is', "$base/css/../App_Data/config.json"); Check 'traversal out of /css/ refused' ($r.Status -eq 404 -and $r.Text -notmatch 'PasswordHash') "$($r.Status)"
    $r = Http @('--path-as-is', "$base/css/..%2fApp_Data%2fconfig.json"); Check 'encoded traversal refused' ($r.Status -eq 404 -and $r.Text -notmatch 'PasswordHash') "$($r.Status)"

    Write-Host "-- login (multipart form, cookies, IP throttle)"
    $r = Api '/loginapi' 'login' @{ username = 'admin'; password = 'wrong' }
    $j = Json $r; Check 'wrong password rejected' ($r.Status -eq 400 -and $j.success -eq $false) "$($r.Status) $($r.Text)"
    $r = Http @('-F', 'action=login', '-F', 'username=admin', '-F', 'password=admin', "$base/loginapi")
    Check 'login without X-Hearth header refused' ($r.Status -eq 403) "$($r.Status)"
    $r = Api '/loginapi' 'login' @{ username = 'admin'; password = 'admin' }
    $j = Json $r; Check 'login succeeds' ($r.Status -eq 200 -and $j.success -eq $true) "$($r.Status) $($r.Text)"
    $sc = Header $r 'Set-Cookie'; Check 'session cookie is HttpOnly + SameSite=Lax' ($sc -match 'HttpOnly' -and $sc -match 'SameSite=Lax' -and $sc -match 'expires=') $sc
    Check 'json response with charset' ((Header $r 'Content-Type') -eq 'application/json; charset=utf-8') (Header $r 'Content-Type')

    Write-Host "-- pages while signed in"
    foreach ($p in '/files', '/users', '/settings', '/account', '/ai') {
        $r = Http @("$base$p"); Check "GET $p" ($r.Status -eq 200 -and $r.Text -match '</html>') "$($r.Status)"
    }

    Write-Host "-- file API"
    $d = "e2e-" + [DateTime]::Now.ToString("HHmmssfff")
    $r = Api '/fileapi' 'mkdir' @{ path = ''; name = $d };   $j = Json $r; Check 'mkdir' ($j.success) $r.Text
    $r = Api '/fileapi' 'newfile' @{ path = $d; name = 'hello.txt' }; $j = Json $r; Check 'newfile' ($j.success) $r.Text
    $text = "Hello from cshttp`nline 2 <b>&amp; ünïcödé 世界</b>"
    $r = Api '/fileapi' 'save' @{ path = "$d/hello.txt"; content = $text }; $j = Json $r; Check 'save (unvalidated HTML + unicode)' ($j.success) $r.Text
    $r = Http @("$base/fileapi?action=read&path=$d/hello.txt"); $j = Json $r
    Check 'read back identical' ($j.success -and $j.data.Content -eq $text) "$($r.Text)"
    $r = Http @("$base/fileapi?action=list&path=$d"); $j = Json $r
    Check 'list shows the file' ($j.success -and ($j.items | ? { $_.Name -eq 'hello.txt' })) $r.Text

    $data = New-Object byte[] (3MB + 123); (New-Object Random 42).NextBytes($data)
    $src = Join-Path $tmp 'upload.bin'; [IO.File]::WriteAllBytes($src, $data)
    $r = Http @('-H', 'X-Hearth: 1', '-F', 'action=upload-chunk', '-F', 'uploadId=e2etest0001', '-F', 'index=0', '-F', 'total=1', '-F', "dest=$d/upload.bin", '-F', "chunk=@$src;type=application/octet-stream;filename=blob", "$base/fileapi")
    $j = Json $r; Check 'chunked upload (3 MB multipart file)' ($j.success) $r.Text

    $half = [int]($data.Length / 2)
    [IO.File]::WriteAllBytes("$tmp\p0.bin", $data[0..($half - 1)]); [IO.File]::WriteAllBytes("$tmp\p1.bin", $data[$half..($data.Length - 1)])
    $r0 = Http @('-H', 'X-Hearth: 1', '-F', 'action=upload-chunk', '-F', 'uploadId=e2etest0002', '-F', 'index=0', '-F', 'total=2', '-F', "dest=$d/two-part.bin", '-F', "chunk=@$tmp\p0.bin;filename=blob", "$base/fileapi")
    $r1 = Http @('-H', 'X-Hearth: 1', '-F', 'action=upload-chunk', '-F', 'uploadId=e2etest0002', '-F', 'index=1', '-F', 'total=2', '-F', "dest=$d/two-part.bin", '-F', "chunk=@$tmp\p1.bin;filename=blob", "$base/fileapi")
    Check 'two-chunk upload' ((Json $r0).success -and (Json $r1).success) "$($r0.Text) $($r1.Text)"

    $r = Http @("$base/fileapi?action=download&path=$d/upload.bin")
    Check 'download (TransmitFile, unbuffered, explicit Content-Length) matches' ($r.Status -eq 200 -and (Sha $r.Bytes) -eq (Sha $data)) "$($r.Status) len=$($r.Bytes.Length)"
    Check 'download headers' ((Header $r 'Content-Disposition') -match 'attachment' -and (Header $r 'Content-Length') -eq "$($data.Length)") (Header $r 'Content-Disposition')
    $r = Http @("$base/fileapi?action=download&path=$d/two-part.bin")
    Check 'two-chunk upload reassembled correctly' ((Sha $r.Bytes) -eq (Sha $data)) "len=$($r.Bytes.Length)"

    $r = Http @("$base/fileapi?action=raw&path=$d/hello.txt")
    $etag = Header $r 'ETag'
    Check 'raw preview with Response.Cache (private, 7 days) and CSP' ($r.Status -eq 200 -and (Header $r 'Cache-Control') -eq 'private, max-age=604800' -and (Header $r 'Content-Security-Policy') -match 'sandbox') "$(Header $r 'Cache-Control')"
    $r = Http @('-H', (Q "If-None-Match: $etag"), "$base/fileapi?action=raw&path=$d/hello.txt")
    Check 'raw revalidation gives 304 with empty body' ($r.Status -eq 304 -and $r.Bytes.Length -eq 0) "$($r.Status)"

    $r = Api '/fileapi' 'zip' @{ paths = "[`"$d/hello.txt`",`"$d/upload.bin`"]"; name = 'bundle.zip' }; Check 'zip' ((Json $r).success) $r.Text
    $r = Http @("$base/fileapi?action=download&paths=%5B%22$d%2Fhello.txt%22%2C%22$d%2Fupload.bin%22%5D")
    $isZip = $r.Bytes.Length -gt 4 -and $r.Bytes[0] -eq 0x50 -and $r.Bytes[1] -eq 0x4B
    Check 'multi-file download builds a zip (WriteFile + Flush)' ($r.Status -eq 200 -and $isZip -and (Header $r 'Content-Type') -eq 'application/zip') "$($r.Status) $($r.Bytes.Length)"

    $r = Api '/fileapi' 'rename' @{ path = "$d/hello.txt"; name = 'renamed.txt' }; Check 'rename' ((Json $r).success) $r.Text
    $r = Http @("$base/fileapi?action=read&path=$d/hello.txt"); Check 'old name gone (400 json error)' ($r.Status -eq 400 -and (Json $r).success -eq $false) "$($r.Status)"
    $r = Http @('-X', 'POST', '-H', 'X-Hearth: 1', '-d', "action=list&path=$d", "$base/fileapi")
    Check 'urlencoded POST also works' ((Json $r).success) $r.Text
    $r = Api '/fileapi' 'delete' @{ paths = "[`"$d`"]" }; Check 'delete to recycle bin' ((Json $r).success) $r.Text

    Write-Host "-- protocol edge cases through the real app"
    $r = Http @('-H', 'X-Hearth: 1', '-H', 'Expect: 100-continue', '-F', 'action=list', '-F', 'path=', "$base/fileapi"); Check 'Expect: 100-continue' ((Json $r).success) $r.Text
    $r = Http @('-I', "$base/files"); Check 'HEAD request (headers only, valid framing)' ($r.Status -eq 200 -and ((Header $r 'Content-Length') -match '^\d+$' -or (Header $r 'Transfer-Encoding') -eq 'chunked')) "$($r.Status)"
    $big = 'x' * 17MB; [IO.File]::WriteAllText("$tmp\big.txt", $big)
    $r = Http @('-H', 'X-Hearth: 1', '-F', 'action=upload-chunk', '-F', "chunk=@$tmp\big.txt", "$base/fileapi"); Check 'request over 16 MB refused with 413' ($r.Status -eq 413 -or $r.Status -eq 0) "$($r.Status)"

    Write-Host "-- logout"
    $r = Http @("$base/logout");            Check 'logout redirects' ($r.Status -eq 302) "$($r.Status)"
    $r = Http @("$base/fileapi?action=list&path="); Check 'API refuses after logout (401)' ($r.Status -eq 401) "$($r.Status) $($r.Text)"
}
finally {
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
    $errs = if (Test-Path "$log.err") { Get-Content "$log.err" -Raw } else { '' }
    if ($errs) { Write-Host "server stderr:"; Write-Host $errs }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host ""
Write-Host "$($script:pass) passed, $($script:fail) failed"
if ($script:fail -gt 0) { exit 1 }
