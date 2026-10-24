// WPF と Windows Forms の両方を参照しているため、
// 名前が衝突する型は WPF 側に寄せる（NotifyIcon 等は TrayService でのみ使用）。
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ComboBox = System.Windows.Controls.ComboBox;
global using Button = System.Windows.Controls.Button;
