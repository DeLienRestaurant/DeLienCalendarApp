// ImplicitUsings を無効化しているため、共通の using をここで宣言する。
// WinForms（System.Windows.Forms / System.Drawing）は暗黙に読み込まない。
// これにより WPF と名前が衝突する型（Brush / Color / RadioButton / Orientation など）を
// 各ファイルでエイリアスする必要がなくなる。

global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;

// 必要な場面だけ WPF の型を使う
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage = System.Windows.MessageBoxImage;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ComboBox = System.Windows.Controls.ComboBox;
global using Button = System.Windows.Controls.Button;
global using UserControl = System.Windows.Controls.UserControl;
global using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
global using Brush = System.Windows.Media.Brush;
global using Color = System.Windows.Media.Color;
global using RadioButton = System.Windows.Controls.RadioButton;
global using Orientation = System.Windows.Controls.Orientation;
