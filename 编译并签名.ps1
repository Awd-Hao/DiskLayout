<#
  编译 + 签名 一键脚本
  ------------------------------------------------------------
  为什么要签名：不签名的 exe 每次运行都会被 Windows 提示"无法验证发布者"。
  这里用一张自签名证书签它，并把证书装进本机的「受信任的根」和「受信任的发布者」，
  之后双击就不会再弹那个框了。

  用法：把本文件放在源码目录（和 DiskLayoutGui.cs 同级），右键"使用 PowerShell 运行"。
  重新编译后必须重新运行一次 —— 每次编译产物的哈希都变，旧签名就失效了。
#>

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "找不到 csc.exe：$csc" }

Write-Host "[1/3] 编译…" -ForegroundColor Cyan
& $csc -target:winexe -nologo -optimize+ -platform:x64 -codepage:65001 `
    -win32icon:app.ico `
    -r:System.Windows.Forms.dll -r:System.Drawing.dll `
    -out:DiskLayout.exe DiskLayoutGui.cs
if ($LASTEXITCODE -ne 0) { throw "编译失败（退出码 $LASTEXITCODE）" }

Write-Host "[2/3] 查找签名证书…" -ForegroundColor Cyan
$cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Subject -like '*DiskLayout*' } |
        Sort-Object NotAfter -Descending | Select-Object -First 1

if (-not $cert) {
    Write-Host "    没有找到证书，创建一张（有效期 10 年）…" -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate -Type CodeSigningCert `
        -Subject "CN=DiskLayout (个人项目)" `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(10) `
        -KeyUsage DigitalSignature -KeyExportPolicy Exportable
    $cerPath = Join-Path $PSScriptRoot 'DiskLayout.cer'
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    # 装进本机信任链（需要管理员；失败也不影响本次签名，只是还会弹提示）
    foreach ($store in 'Root', 'TrustedPublisher') {
        try { Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null }
        catch { Write-Host "    导入 $store 失败（需要管理员）：$($_.Exception.Message)" -ForegroundColor Yellow }
    }
    Write-Host "    证书已创建：$($cert.Thumbprint)" -ForegroundColor Green
}

Write-Host "[3/3] 签名…" -ForegroundColor Cyan
Set-AuthenticodeSignature -FilePath (Join-Path $PSScriptRoot 'DiskLayout.exe') -Certificate $cert | Out-Null
$sig = Get-AuthenticodeSignature (Join-Path $PSScriptRoot 'DiskLayout.exe')
Write-Host "    签名状态：$($sig.Status)   签名者：$($sig.SignerCertificate.Subject)" `
    -ForegroundColor $(if ($sig.Status -eq 'Valid') { 'Green' } else { 'Red' })

Write-Host ""
Write-Host "完成。" -ForegroundColor Green
Write-Host "提醒：把 exe 复制到「下载」以外的地方（C:\ 或 D:\）再运行，" -ForegroundColor Yellow
Write-Host "      否则系统会拒绝它访问磁盘卷（错误码 5）。" -ForegroundColor Yellow
