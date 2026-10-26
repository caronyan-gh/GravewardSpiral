using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace GravewardSpiral.Editor;

/// <summary>
/// エディタの見た目(ライトモード)。色と、ボタン・入力欄・タブ・一覧・表のスタイルを、アプリ全体に入れる。
/// 色は Palette に集め、コードからも同じ色を使う。
/// </summary>
public static class Theme
{
    public static class Palette
    {
        public const string Background = "#F4F6FA";
        public const string Surface = "#FFFFFF";
        public const string Border = "#DDE2EA";
        public const string BorderStrong = "#C3CAD5";
        public const string Text = "#1F2733";
        public const string TextDim = "#6B7585";
        public const string Accent = "#3B5BDB";
        public const string AccentHover = "#324FC4";
        public const string AccentSoft = "#E8EDFD";
        public const string Hover = "#F0F3F8";
        public const string Good = "#2F9E44";
        public const string Warn = "#D9480F";
        public const string HeaderTop = "#2B3A67";
        public const string HeaderBottom = "#3B5BDB";
    }

    public static Brush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    /// <summary>アイコンの字体(Windows 11 の Segoe Fluent Icons。無ければ MDL2)。</summary>
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static void Apply(Application app)
    {
        // %名前% を Palette の色にする
        string xaml = typeof(Palette).GetFields().Aggregate(Xaml, (x, f) => x.Replace($"%{f.Name}%", (string)f.GetValue(null)!));
        var dict = (ResourceDictionary)XamlReader.Parse(xaml);
        app.Resources.MergedDictionaries.Add(dict);
    }

    private const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <SolidColorBrush x:Key="Bg" Color="%Background%"/>
  <SolidColorBrush x:Key="Surface" Color="%Surface%"/>
  <SolidColorBrush x:Key="Line" Color="%Border%"/>
  <SolidColorBrush x:Key="LineStrong" Color="%BorderStrong%"/>
  <SolidColorBrush x:Key="Ink" Color="%Text%"/>
  <SolidColorBrush x:Key="InkDim" Color="%TextDim%"/>
  <SolidColorBrush x:Key="Accent" Color="%Accent%"/>
  <SolidColorBrush x:Key="AccentHover" Color="%AccentHover%"/>
  <SolidColorBrush x:Key="AccentSoft" Color="%AccentSoft%"/>
  <SolidColorBrush x:Key="Hover" Color="%Hover%"/>

  <Style TargetType="Window">
    <Setter Property="FontFamily" Value="Yu Gothic UI, Segoe UI"/>
  </Style>

  <!-- ボタン: 白地に細い枠、角丸。触ると淡く、押すと少し濃く -->
  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource LineStrong}"/>
    <Setter Property="Padding" Value="12,5"/>
    <Setter Property="Margin" Value="0,2,6,2"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6"
                  Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="b" Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- パレットの1つ(迷宮の筆と道具): 押してあるものは青地に白い字 -->
  <Style x:Key="PaletteButton" TargetType="RadioButton">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource LineStrong}"/>
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="Margin" Value="0,2,4,2"/>
    <Setter Property="MinWidth" Value="64"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="6"
                  Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource Accent}"/>
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
              <Setter Property="Foreground" Value="White"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 道具の窓の小さな四角(ペイントの道具のように)。押してあるものは淡い青地に青い枠 -->
  <Style x:Key="ToolButton" TargetType="RadioButton">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Line}"/>
    <Setter Property="Width" Value="34"/>
    <Setter Property="Height" Value="34"/>
    <Setter Property="Margin" Value="0,0,0,3"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="RadioButton">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="5">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
              <Setter TargetName="b" Property="BorderThickness" Value="2"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 目立たせるボタン(保存・試し遊びなど) -->
  <Style x:Key="AccentButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="Background" Value="{StaticResource Accent}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Accent}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderThickness="0" CornerRadius="6" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource AccentHover}"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="b" Property="Opacity" Value="0.85"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 上の帯のボタン(濃い地に、白い字) -->
  <Style x:Key="BarButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="Background" Value="#26FFFFFF"/>
    <Setter Property="Padding" Value="12,6"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}">
            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="b" Property="Background" Value="#47FFFFFF"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="b" Property="Background" Value="#66FFFFFF"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 入力欄: 角丸、選ぶと青い枠 -->
  <Style TargetType="TextBox">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource LineStrong}"/>
    <Setter Property="Padding" Value="6,4"/>
    <Setter Property="Margin" Value="0,1"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="5">
            <ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
              <Setter TargetName="b" Property="BorderThickness" Value="1.5"/>
            </Trigger>
            <Trigger Property="IsReadOnly" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource Bg}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ComboBox">
    <Setter Property="Padding" Value="6,4"/>
    <Setter Property="Margin" Value="0,1"/>
  </Style>

  <!-- タブ: 文字だけ。選んでいるタブは青い字と下線 -->
  <Style TargetType="TabControl">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="0"/>
  </Style>
  <Style TargetType="TabItem">
    <Setter Property="Foreground" Value="{StaticResource InkDim}"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TabItem">
          <Border x:Name="b" Padding="14,9,14,7" Background="Transparent" BorderThickness="0,0,0,3" BorderBrush="Transparent">
            <ContentPresenter ContentSource="Header"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
              <Setter Property="Foreground" Value="{StaticResource Accent}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="b" Property="Opacity" Value="0.4"/>
              <Setter Property="Cursor" Value="Arrow"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- 一覧: 白いカード、行に余白、選んだ行は淡い青 -->
  <Style TargetType="ListBox">
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Line}"/>
    <Setter Property="Padding" Value="2"/>
  </Style>
  <Style TargetType="ListBoxItem">
    <Setter Property="Padding" Value="8,5"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListBoxItem">
          <Border x:Name="b" Background="Transparent" CornerRadius="4" Padding="{TemplateBinding Padding}" Margin="1">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource Hover}"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
              <Setter Property="Foreground" Value="{StaticResource Accent}"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="TreeView">
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Line}"/>
    <Setter Property="Padding" Value="4"/>
  </Style>
  <Style TargetType="TreeViewItem">
    <Setter Property="Padding" Value="3,2"/>
  </Style>

  <!-- 表: 横線だけ、見出しは淡い地 -->
  <Style TargetType="DataGrid">
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource Line}"/>
    <Setter Property="GridLinesVisibility" Value="Horizontal"/>
    <Setter Property="HorizontalGridLinesBrush" Value="{StaticResource Line}"/>
    <Setter Property="RowHeaderWidth" Value="0"/>
  </Style>
  <Style TargetType="DataGridColumnHeader">
    <Setter Property="Background" Value="{StaticResource Bg}"/>
    <Setter Property="Foreground" Value="{StaticResource InkDim}"/>
    <Setter Property="Padding" Value="8,6"/>
    <Setter Property="BorderBrush" Value="{StaticResource Line}"/>
    <Setter Property="BorderThickness" Value="0,0,0,1"/>
  </Style>
  <Style TargetType="DataGridCell">
    <Setter Property="Padding" Value="6,4"/>
    <Setter Property="BorderThickness" Value="0"/>
  </Style>

  <Style TargetType="CheckBox">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
  </Style>
  <Style TargetType="RadioButton">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
  </Style>
  <Style TargetType="ToolTip">
    <Setter Property="Background" Value="{StaticResource Ink}"/>
    <Setter Property="Foreground" Value="White"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Padding" Value="8,5"/>
  </Style>
</ResourceDictionary>
""";
}
