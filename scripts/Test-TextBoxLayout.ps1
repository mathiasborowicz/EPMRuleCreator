# Measure the actual shared template in Windows WPF without opening the app or signing in.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
[xml]$applicationXaml = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\EPMRuleCreator.UI\App.xaml'))
$namespaces = [System.Xml.XmlNamespaceManager]::new($applicationXaml.NameTable)
$namespaces.AddNamespace('wpf', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$style = $applicationXaml.SelectSingleNode('//wpf:Style[@TargetType="TextBox"]', $namespaces)
if ($null -eq $style) { throw 'The shared TextBox style is missing.' }
$brushes = ($applicationXaml.SelectNodes('//wpf:Application.Resources/wpf:SolidColorBrush', $namespaces) |
    ForEach-Object { $_.OuterXml }) -join "`n"
$markup = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">' +
    $brushes + $style.OuterXml + '</ResourceDictionary>'
$resources = [System.Windows.Markup.XamlReader]::Parse($markup)

$field = New-Object System.Windows.Controls.TextBox
$field.Style = $resources[[System.Windows.Controls.TextBox]]
# Keep DynamicResource lookups available when the style is applied.
$field.Resources.MergedDictionaries.Add($resources)
$field.Width = 600
$field.Height = 40
$field.Margin = [System.Windows.Thickness]::new(0)
$field.IsReadOnly = $true
$field.VerticalContentAlignment = [System.Windows.VerticalAlignment]::Center
$field.Text = 'C:\Example folder\Example application.exe'
$field.ApplyTemplate() | Out-Null
$field.Measure([System.Windows.Size]::new(600, 40))
$field.Arrange([System.Windows.Rect]::new(0, 0, 600, 40))
$field.UpdateLayout()
$hostViewer = $field.Template.FindName('PART_ContentHost', $field)
if ($null -eq $hostViewer -or $hostViewer.ViewportHeight -lt 18) {
    throw 'The fixed-height file field clips its text: content viewport is too small.'
}
if ($field.ActualHeight -ne 40) { throw 'The file field changed height.' }
Write-Output "TextBox layout regression test passed. Text viewport: $($hostViewer.ViewportHeight) DIPs."
