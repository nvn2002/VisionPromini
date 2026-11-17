using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TunVision.UI.Common;
using TunVision.UI.ViewModels;

namespace TunVision.UI;

public partial class NodeSettingsWindow : Window
{
    private readonly MainViewModel _vm;
    public ICommand CloseCommand { get; }

    public NodeSettingsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        CloseCommand = new RelayCommand(_ => Close());
        Loaded += (_, _) => UpdateModelPreviews();
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.PipelineRunCompleted += OnPipelineRunCompleted;
    }

    private void UpdateModelPreviews()
    {
        if (ShapeMatchModelPreview != null && _vm.CurrentShapeMatchNode != null)
        {
            ShapeMatchModelPreview.UpdateModel(_vm.CurrentShapeMatchNode.Model);
        }
        if (TrainShapeModelPreview != null && _vm.CurrentTrainShapeNode != null)
        {
            TrainShapeModelPreview.UpdateModel(_vm.CurrentTrainShapeNode.ToolInstance.Model);
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentShapeMatchNode) ||
            e.PropertyName == nameof(MainViewModel.CurrentTrainShapeNode) ||
            e.PropertyName == nameof(MainViewModel.SelectedNode))
        {
            UpdateModelPreviews();
        }
    }

    private void OnPipelineRunCompleted(object? sender, System.EventArgs e)
    {
        UpdateModelPreviews();
    }

    private void FlushFocusedTextBoxBinding()
    {
        if (Keyboard.FocusedElement is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox tb)
        {
            tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            _ = _vm.RunPipelineAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            FlushFocusedTextBoxBinding();
        }
        else if (e.Key == Key.Escape)
        {
            FlushFocusedTextBoxBinding();
            Close();
            e.Handled = true;
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.PipelineRunCompleted -= OnPipelineRunCompleted;
        FlushFocusedTextBoxBinding();
        base.OnClosed(e);
    }

    private void OnRunTestClick(object sender, RoutedEventArgs e)
    {
        FlushFocusedTextBoxBinding();
        _ = _vm.RunPipelineAsync();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        FlushFocusedTextBoxBinding();
        Close();
    }
}
