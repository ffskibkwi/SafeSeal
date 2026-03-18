🎨 SafeSeal UI/UX Design Specification (v2.0)1. Design PhilosophyThe interface follows the Fluent Design System principles:Simplicity: Minimalist layout to reduce cognitive load during sensitive tasks.Depth: Subtle drop shadows and layering to define hierarchy without heavy borders.Softness: Standardized 6px to 8px corner radiuses for all containers and buttons.Responsiveness: A fluid "Sidebar + Content + Detail Pane" architecture.2. Visual Identity & PaletteElementHex CodePurposePrimary Accent#2563EBButtons, Active States, BrandingApp Background#F3F3F3Main window backdropSurface#FFFFFFSidebar, Cards, Detail PaneBorder/Divider#E5E5E5Subtle separation of UI elementsText Primary#1A1A1ATitles and body textTypography: * Primary: Segoe UI Variable Display (Windows 11 Standard)Fallback: Microsoft YaHei UI (Optimized for Chinese characters)3. UI Architecture3.1 Sidebar (Navigation)Branding: Bold "SafeSeal" logo in the top-left.Navigation: List of document categories (All, ID Cards, Passports).State: Uses a "Selected" indicator (a small vertical blue bar) similar to Microsoft To Do.3.2 Main Gallery (Document Grid)Card Layout: Documents are displayed as white cards with soft shadows.Content: A simplified icon or a heavily blurred thumbnail (for privacy) with the document name.3.3 Detail Pane (The "Slide-out")Action: Appears from the right when a document is clicked.Function: Houses the real-time watermark preview, text input, and the export button.Security: Triggers the memory-clearing logic immediately upon closing.4. Core XAML Implementation4.1 Global Styles (App.xaml)XML<Style x:Key="FluentNavButton" TargetType="Button">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="BorderThickness" Value="0"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="Padding" Value="15,10"/>
    <Setter Property="Template">
        <Setter.Value>
            <ControlTemplate TargetType="Button">
                <Border Background="{TemplateBinding Background}" CornerRadius="6" Margin="10,2">
                    <ContentPresenter VerticalAlignment="Center" Margin="10,0"/>
                </Border>
                <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                        <Setter Property="Background" Value="#F0F0F0"/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>
4.2 Main Window Layout (MainWindow.xaml)XML<Grid Background="#F3F3F3">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="260"/> <ColumnDefinition Width="*"/>   </Grid.ColumnDefinitions>

    <Border Grid.Column="0" Background="White" BorderBrush="#E5E5E5" BorderThickness="0,0,1,0">
        <StackPanel Margin="0,40,0,0">
            <TextBlock Text="SafeSeal" FontSize="22" FontWeight="Bold" Foreground="#2563EB" Margin="25,0,0,30"/>
            <Button Content="🏠 All Documents" Style="{StaticResource FluentNavButton}"/>
            <Button Content="💳 ID Cards" Style="{StaticResource FluentNavButton}"/>
            <Button Content="🛂 Passports" Style="{StaticResource FluentNavButton}"/>
        </StackPanel>
    </Border>

    <Grid Grid.Column="1" Margin="40">
        <TextBlock Text="All Documents" FontSize="28" FontWeight="SemiBold" VerticalAlignment="Top"/>
        
        <ListBox Background="Transparent" BorderThickness="0" Margin="0,50,0,0">
            <ListBox.ItemsPanel>
                <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
            </ListBox.ItemsPanel>
            <ListBoxItem>
                <Border Width="200" Height="140" Background="White" CornerRadius="8">
                    <Border.Effect>
                        <DropShadowEffect BlurRadius="12" Opacity="0.05" ShadowDepth="2"/>
                    </Border.Effect>
                    <TextBlock Text="ID_Card_Front.seal" VerticalAlignment="Center" HorizontalAlignment="Center"/>
                </Border>
            </ListBoxItem>
        </ListBox>
    </Grid>

    <Border x:Name="DetailPane" Grid.Column="1" HorizontalAlignment="Right" Width="380" 
            Background="White" Visibility="Collapsed" BorderBrush="#E5E5E5" BorderThickness="1,0,0,0">
        <Grid Margin="25">
            <StackPanel>
                <Button Content="✕ Close" HorizontalAlignment="Right" Click="CloseDetail_Click"/>
                <TextBlock Text="Watermark Settings" FontSize="20" FontWeight="Bold" Margin="0,20,0,15"/>
                
                <Border CornerRadius="6" Background="#F9F9F9" Height="200" Margin="0,0,0,20">
                    <Image x:Name="LivePreview" Stretch="Uniform"/>
                </Border>

                <TextBlock Text="Custom Text" FontWeight="Medium" Margin="0,0,0,5"/>
                <TextBox x:Name="WatermarkText" Text="FOR USE ONLY ON {Date}"/>
                
                <Button Content="Export Image" Margin="0,40,0,0" Height="40" Background="#2563EB" Foreground="White"/>
            </StackPanel>
        </Grid>
    </Border>
</Grid>
5. Interaction & Logic (C#)5.1 Dynamic MacrosThe UI automatically parses the following macros in the WatermarkText input:{Date} $\rightarrow$ Current local date (2026-03-18).{Time} $\rightarrow$ Current local time (16:47).{User} $\rightarrow$ Current Windows User Profile name.5.2 Secure Lifecycle ManagementTo ensure strict local privacy as per Spec v2.0:Selection: When a user clicks a document, the encrypted buffer is loaded into a Pinned array.Rendering: The DrawingContext renders the watermark on top of the decrypted stream.Disposal: When DetailPane visibility is set to Collapsed, Array.Clear() is immediately invoked on the sensitive buffer.6. Export Filename ConventionGenerated files will follow this strict naming pattern to prevent overwriting originals:SafeSeal_Export_[OriginalName]_[YYYYMMDD_HHMM].jpg