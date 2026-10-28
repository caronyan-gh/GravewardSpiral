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

  <!-- 縦横共通のスクロールバー。Trackでドラッグとページ送りを保つ。 -->
  <Style x:Key="ScrollThumb" TargetType="Thumb">
    <Setter Property="Background" Value="{StaticResource LineStrong}"/>
    <Setter Property="MinHeight" Value="18"/>
    <Setter Property="MinWidth" Value="6"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Thumb">
          <Border x:Name="grip" Background="{TemplateBinding Background}" CornerRadius="4" Margin="2"/>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="grip" Property="Background" Value="{StaticResource Accent}"/></Trigger>
            <Trigger Property="IsDragging" Value="True"><Setter TargetName="grip" Property="Background" Value="{StaticResource AccentHover}"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>
  <Style x:Key="ScrollPage" TargetType="RepeatButton">
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="ScrollBar">
    <Setter Property="Width" Value="12"/>
    <Setter Property="Background" Value="{StaticResource Bg}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Border Background="{TemplateBinding Background}" CornerRadius="6">
            <Track x:Name="PART_Track" Orientation="Vertical" IsDirectionReversed="True">
              <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageUpCommand"/></Track.DecreaseRepeatButton>
              <Track.Thumb><Thumb Style="{StaticResource ScrollThumb}"/></Track.Thumb>
              <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageDownCommand"/></Track.IncreaseRepeatButton>
            </Track>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Horizontal">
        <Setter Property="Width" Value="Auto"/>
        <Setter Property="Height" Value="12"/>
        <Setter Property="Template">
          <Setter.Value>
            <ControlTemplate TargetType="ScrollBar">
              <Border Background="{TemplateBinding Background}" CornerRadius="6">
                <Track x:Name="PART_Track" Orientation="Horizontal" IsDirectionReversed="False">
                  <Track.DecreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageLeftCommand"/></Track.DecreaseRepeatButton>
                  <Track.Thumb><Thumb Style="{StaticResource ScrollThumb}" MinHeight="6" MinWidth="18"/></Track.Thumb>
                  <Track.IncreaseRepeatButton><RepeatButton Style="{StaticResource ScrollPage}" Command="ScrollBar.PageRightCommand"/></Track.IncreaseRepeatButton>
                </Track>
              </Border>
            </ControlTemplate>
          </Setter.Value>
        </Setter>
      </Trigger>
      <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
    </Style.Triggers>
  </Style>

  <!-- 開閉の状態を表す: 右向きは閉じた枝、下向きは開いた枝。 -->
  <Style TargetType="Expander">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Expander">
          <StackPanel>
            <ToggleButton x:Name="HeaderToggle" Focusable="True" Cursor="Hand"
                          IsChecked="{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"
                          Content="{TemplateBinding Header}" ContentTemplate="{TemplateBinding HeaderTemplate}"
                          Foreground="{TemplateBinding Foreground}" FontWeight="{TemplateBinding FontWeight}">
              <ToggleButton.Template>
                <ControlTemplate TargetType="ToggleButton">
                  <Border x:Name="HeaderBorder" Background="Transparent" CornerRadius="4" Padding="3,5">
                    <DockPanel>
                      <Path x:Name="Arrow" Data="M 0 0 L 5 5 L 0 10" Stroke="{TemplateBinding Foreground}" StrokeThickness="1.5"
                            Width="10" Height="10" Margin="2,0,8,0" VerticalAlignment="Center"/>
                      <ContentPresenter VerticalAlignment="Center" RecognizesAccessKey="True"/>
                    </DockPanel>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsChecked" Value="True"><Setter TargetName="Arrow" Property="Data" Value="M 0 2 L 5 7 L 10 2"/></Trigger>
                    <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="HeaderBorder" Property="Background" Value="{StaticResource Hover}"/></Trigger>
                    <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="HeaderBorder" Property="Background" Value="{StaticResource AccentSoft}"/></Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter x:Name="Body" Visibility="Collapsed" Margin="0,4,0,0"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsExpanded" Value="True"><Setter TargetName="Body" Property="Visibility" Value="Visible"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- ボタン: 白地に細い枠、角丸。触ると淡く、押すと少し濃く -->
  <Style TargetType="Button">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource LineStrong}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="12,5"/>
    <Setter Property="Margin" Value="0,2,6,2"/>
    <Setter Property="Cursor" Value="Hand"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="Button">
          <Border x:Name="b" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6"
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
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <!-- Template を替える AccentButton / BarButton も、無効なときは同じ見た目にする -->
      <Trigger Property="IsEnabled" Value="False">
        <Setter Property="Opacity" Value="0.5"/>
        <Setter Property="Cursor" Value="Arrow"/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <!-- 入力欄のすぐ横に置く操作。普通のボタンの見た目を引き継ぎ、余白だけ揃える -->
  <Style x:Key="InlineButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}">
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="Margin" Value="6,1,0,1"/>
    <Setter Property="VerticalAlignment" Value="Center"/>
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

  <!-- 編集できるコンボの中は二重の枠にしない。PART_ContentHost は文字入力と IME に必要 -->
  <Style x:Key="ComboBoxEditor" TargetType="TextBox">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="TextBox">
          <ScrollViewer x:Name="PART_ContentHost"/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ComboBoxItem">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Padding" Value="8,5"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBoxItem">
          <Border x:Name="b" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}" Margin="1">
            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
              <Setter Property="Foreground" Value="{StaticResource Accent}"/>
            </Trigger>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="b" Property="Background" Value="{StaticResource AccentSoft}"/>
              <Setter Property="Foreground" Value="{StaticResource Accent}"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="b" Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="ComboBox">
    <Setter Property="Foreground" Value="{StaticResource Ink}"/>
    <Setter Property="Background" Value="{StaticResource Surface}"/>
    <Setter Property="BorderBrush" Value="{StaticResource LineStrong}"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="6,4"/>
    <Setter Property="Margin" Value="0,1"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Auto"/>
    <Setter Property="ScrollViewer.VerticalScrollBarVisibility" Value="Auto"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ComboBox">
          <Grid x:Name="root" SnapsToDevicePixels="True">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="26"/>
            </Grid.ColumnDefinitions>
            <Border x:Name="b" Grid.ColumnSpan="2" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="5"/>
            <ToggleButton Grid.ColumnSpan="2" Focusable="False" ClickMode="Press"
                          IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
              <ToggleButton.Template>
                <ControlTemplate TargetType="ToggleButton">
                  <Border Background="Transparent">
                    <Path Data="M 0,0 L 4,4 L 8,0" Stroke="{StaticResource InkDim}" StrokeThickness="1.5"
                          StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,9,0"/>
                  </Border>
                </ControlTemplate>
              </ToggleButton.Template>
            </ToggleButton>
            <ContentPresenter x:Name="selection" IsHitTestVisible="False" Margin="{TemplateBinding Padding}" ClipToBounds="True"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}" HorizontalAlignment="Stretch"
                              Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                              ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
                              ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"/>
            <TextBox x:Name="PART_EditableTextBox" Style="{StaticResource ComboBoxEditor}" Visibility="Hidden"
                     Margin="{TemplateBinding Padding}" VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
                     Foreground="{TemplateBinding Foreground}" IsReadOnly="{TemplateBinding IsReadOnly}"/>
            <Popup x:Name="PART_Popup" Grid.ColumnSpan="2" Placement="Bottom" AllowsTransparency="True" Focusable="False"
                   IsOpen="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}">
              <Border Background="{StaticResource Surface}" BorderBrush="{StaticResource LineStrong}" BorderThickness="1" CornerRadius="5" Padding="3"
                      MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}" MaxHeight="{TemplateBinding MaxDropDownHeight}">
                <ScrollViewer CanContentScroll="True" SnapsToDevicePixels="True">
                  <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
            </Trigger>
            <Trigger Property="IsDropDownOpen" Value="True">
              <Setter TargetName="b" Property="BorderBrush" Value="{StaticResource Accent}"/>
            </Trigger>
            <Trigger Property="IsEditable" Value="True">
              <Setter Property="IsTabStop" Value="False"/>
              <Setter TargetName="selection" Property="Visibility" Value="Hidden"/>
              <Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter TargetName="root" Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
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
