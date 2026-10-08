param([Parameter(Mandatory)] [string] $Work)
# Does a WebResourceRequested response override a virtual-host folder mapping (the library.memento filter)?
$ErrorActionPreference = 'Stop'
$lib = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.web.webview2\1.0.4258.31'
Add-Type -Path (Join-Path $lib 'lib\net462\Microsoft.Web.WebView2.Core.dll')
Add-Type -Path (Join-Path $lib 'lib\net462\Microsoft.Web.WebView2.WinForms.dll')
$env:PATH = (Join-Path $lib 'runtimes\win-x64\native') + ';' + $env:PATH
Add-Type -AssemblyName System.Windows.Forms
$app = Join-Path $Work 'app'; $data = Join-Path $Work 'data'
New-Item -ItemType Directory -Force $app, $data | Out-Null
Set-Content (Join-Path $data 'ok.json') '{"ok":true}'
Set-Content (Join-Path $data 'secret.json') '{"secret":true}'
Set-Content (Join-Path $app 'index.html') @'
<!doctype html><html><head><meta charset="utf-8"><script src="probe.js"></script></head><body>probe</body></html>
'@
Set-Content (Join-Path $app 'probe.js') @'
(async () => {
  const out = {};
  for (const name of ['ok.json', 'secret.json', 'missing.json']) {
    try { const r = await fetch('https://lib.test/' + name); out[name] = r.status + ' ' + (await r.text()).trim(); }
    catch (e) { out[name] = 'error ' + e; }
  }
  chrome.webview.postMessage(JSON.stringify(out));
})();
'@
$form = New-Object System.Windows.Forms.Form
$form.Width = 400; $form.Height = 300; $form.ShowInTaskbar = $false
$view = New-Object Microsoft.Web.WebView2.WinForms.WebView2
$view.Dock = 'Fill'
$form.Controls.Add($view)
$script:result = 'no answer'
$form.add_Shown({
  $task = [Microsoft.Web.WebView2.Core.CoreWebView2Environment]::CreateAsync($null, (Join-Path $Work 'ud'), $null)
  while (-not $task.IsCompleted) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
  $init = $view.EnsureCoreWebView2Async($task.Result)
  while (-not $init.IsCompleted) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 20 }
  $core = $view.CoreWebView2
  $core.SetVirtualHostNameToFolderMapping('app.test', $app, [Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind]::DenyCors)
  $core.SetVirtualHostNameToFolderMapping('lib.test', $data, [Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind]::Allow)
  $core.AddWebResourceRequestedFilter('https://lib.test/*', [Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext]::All)
  $script:envRef = $task.Result
  $script:seenUris = New-Object System.Collections.ArrayList
  $core.add_WebResourceRequested({ param($s, $e)
    [void]$script:seenUris.Add($e.Request.Uri)
    if ($e.Request.Uri -like '*secret.json') { $e.Response = $script:envRef.CreateWebResourceResponse($null, 404, 'Not Found', '') }
  })
  $core.add_WebMessageReceived({ param($s, $e) $script:result = $e.TryGetWebMessageAsString(); $form.Close() })
  $core.Navigate('https://app.test/index.html')
})
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 30000
$timer.add_Tick({ $form.Close() })
$timer.Start()
[System.Windows.Forms.Application]::Run($form)
"result: $script:result"
"handler saw: $($script:seenUris -join ', ')"
