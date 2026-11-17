using System;
using System.Windows;
using System.Windows.Input;
using TunVision.UI.ViewModels;

namespace TunVision.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private NodeSettingsWindow? _settingsWindow;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        _vm.NodeSettingsRequested += OnNodeSettingsRequested;
        DataContext = _vm;
    }

    private void OnNodeSettingsRequested(object? sender, EventArgs e)
    {
        OpenOrActivateSettingsWindow();
    }

    private void OnNodeTableDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.SelectedNode != null)
        {
            OpenOrActivateSettingsWindow();
        }
    }

    private void OpenOrActivateSettingsWindow()
    {
        if (_vm.SelectedNode == null && _vm.GraphNodes.Count > 0)
        {
            _vm.SelectedNode = _vm.GraphNodes[0];
        }

        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new NodeSettingsWindow(_vm)
            {
                Owner = this
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else
        {
            if (_settingsWindow.WindowState == WindowState.Minimized)
                _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
        }
    }

    private void OnFitGraphClick(object sender, RoutedEventArgs e)
    {
        GraphCanvasCtrl.FitGraphToScreen();
    }

    private void OnFitAllClick(object sender, RoutedEventArgs e)
    {
        VisionCanvasCtrl.FitToScreen();
        GraphCanvasCtrl.FitGraphToScreen();
    }

    private void OnNewPipelineDropDownClick(object sender, RoutedEventArgs e)
    {
        if (NewPipelineBtn.ContextMenu != null)
        {
            NewPipelineBtn.ContextMenu.PlacementTarget = NewPipelineBtn;
            NewPipelineBtn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            NewPipelineBtn.ContextMenu.DataContext = _vm;
            NewPipelineBtn.ContextMenu.IsOpen = true;
        }
    }
}
