# pixivShelf 卸载入口
# 由 uninstall.cmd 调用；勾选复选框才会一并删除配置与任务记录。

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$appDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$unins = Join-Path $appDir 'unins000.exe'

$form = New-Object System.Windows.Forms.Form
$form.Text = '卸载 pixivShelf'
$form.ClientSize = New-Object System.Drawing.Size(460, 190)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.ShowIcon = $false
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 9)

$label = New-Object System.Windows.Forms.Label
$label.Text = '将删除程序文件、开始菜单文件夹和桌面快捷方式。你下载的图片不会被删除。'
$label.SetBounds(16, 16, 428, 44)
$form.Controls.Add($label)

$check = New-Object System.Windows.Forms.CheckBox
$check.Text = '同时删除配置、登录状态和任务记录（data 目录）'
$check.SetBounds(16, 76, 428, 24)
$check.Checked = $false
$form.Controls.Add($check)

$ok = New-Object System.Windows.Forms.Button
$ok.Text = '卸载'
$ok.SetBounds(252, 136, 90, 30)
$ok.DialogResult = [System.Windows.Forms.DialogResult]::OK
$form.AcceptButton = $ok
$form.Controls.Add($ok)

$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = '取消'
$cancel.SetBounds(354, 136, 90, 30)
$cancel.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.CancelButton = $cancel
$form.Controls.Add($cancel)

$result = $form.ShowDialog()

# 取消：什么都不做
if ($result -ne [System.Windows.Forms.DialogResult]::OK) {
    exit 0
}

if ($check.Checked) {
    foreach ($path in @((Join-Path $appDir 'data'), (Join-Path $env:LOCALAPPDATA 'PixivShelf'))) {
        if (Test-Path $path) {
            Remove-Item $path -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if (Test-Path $unins) {
    Start-Process -FilePath $unins `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' `
        -Wait
}
