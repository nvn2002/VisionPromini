using TunVision.Core.Abstractions;
using TunVision.UI.Common;

namespace TunVision.UI.ViewModels;

public class ToolItemViewModel : ObservableObject
{
    public IVisionTool Tool { get; }

    public string Name
    {
        get => Tool.Name;
        set
        {
            if (Tool.Name != value)
            {
                Tool.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public bool Enabled
    {
        get => Tool.Enabled;
        set
        {
            if (Tool.Enabled != value)
            {
                Tool.Enabled = value;
                OnPropertyChanged();
            }
        }
    }

    private string _status = "Ready";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private double _lastExecutionTimeMs;
    public double LastExecutionTimeMs
    {
        get => _lastExecutionTimeMs;
        set => SetProperty(ref _lastExecutionTimeMs, value);
    }

    public ToolItemViewModel(IVisionTool tool)
    {
        Tool = tool;
    }
}
