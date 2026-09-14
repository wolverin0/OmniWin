using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using OmniWin.Core.Services;

namespace OmniWin.UI.Views;

public class RepairStepDisplayItem : INotifyPropertyChanged
{
    private int _stepNumber;
    private string _title = string.Empty;
    private string _command = string.Empty;
    private string _description = string.Empty;
    private string _statusText = "Pendiente ⏳";
    private RepairStepStatus _status = RepairStepStatus.Pending;

    private SolidColorBrush _statusColor = FreezeBrush("#94A3B8");
    private SolidColorBrush _statusBg = FreezeBrush("#1E293B");
    private SolidColorBrush _cardBackground = FreezeBrush("#090D16");
    private SolidColorBrush _cardBorderBrush = FreezeBrush("#1E293B");
    private SolidColorBrush _numberBadgeBg = FreezeBrush("#1E293B");
    private SolidColorBrush _numberBadgeFg = FreezeBrush("#94A3B8");

    public int StepNumber
    {
        get => _stepNumber;
        set { _stepNumber = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Command
    {
        get => _command;
        set { _command = value; OnPropertyChanged(); }
    }

    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public RepairStepStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public SolidColorBrush StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; OnPropertyChanged(); }
    }

    public SolidColorBrush StatusBg
    {
        get => _statusBg;
        set { _statusBg = value; OnPropertyChanged(); }
    }

    public SolidColorBrush CardBackground
    {
        get => _cardBackground;
        set { _cardBackground = value; OnPropertyChanged(); }
    }

    public SolidColorBrush CardBorderBrush
    {
        get => _cardBorderBrush;
        set { _cardBorderBrush = value; OnPropertyChanged(); }
    }

    public SolidColorBrush NumberBadgeBg
    {
        get => _numberBadgeBg;
        set { _numberBadgeBg = value; OnPropertyChanged(); }
    }

    public SolidColorBrush NumberBadgeFg
    {
        get => _numberBadgeFg;
        set { _numberBadgeFg = value; OnPropertyChanged(); }
    }

    public void UpdateStatus(RepairStepStatus status, string message)
    {
        Status = status;
        StatusText = message;

        switch (status)
        {
            case RepairStepStatus.Pending:
                StatusColor = FreezeBrush("#94A3B8");
                StatusBg = FreezeBrush("#1E293B");
                CardBackground = FreezeBrush("#090D16");
                CardBorderBrush = FreezeBrush("#1E293B");
                NumberBadgeBg = FreezeBrush("#1E293B");
                NumberBadgeFg = FreezeBrush("#94A3B8");
                break;

            case RepairStepStatus.Running:
                StatusColor = FreezeBrush("#60A5FA");
                StatusBg = FreezeBrush("#0C4A6E");
                CardBackground = FreezeBrush("#0C182E");
                CardBorderBrush = FreezeBrush("#0284C7");
                NumberBadgeBg = FreezeBrush("#0284C7");
                NumberBadgeFg = FreezeBrush("#FFFFFF");
                break;

            case RepairStepStatus.Completed:
                StatusColor = FreezeBrush("#34D399");
                StatusBg = FreezeBrush("#064E3B");
                CardBackground = FreezeBrush("#090D16");
                CardBorderBrush = FreezeBrush("#065F46");
                NumberBadgeBg = FreezeBrush("#064E3B");
                NumberBadgeFg = FreezeBrush("#34D399");
                break;

            case RepairStepStatus.Error:
                StatusColor = FreezeBrush("#F87171");
                StatusBg = FreezeBrush("#450A0A");
                CardBackground = FreezeBrush("#1C0F12");
                CardBorderBrush = FreezeBrush("#7F1D1D");
                NumberBadgeBg = FreezeBrush("#7F1D1D");
                NumberBadgeFg = FreezeBrush("#FCA5A5");
                break;

            case RepairStepStatus.Skipped:
                StatusColor = FreezeBrush("#FBBF24");
                StatusBg = FreezeBrush("#451A03");
                CardBackground = FreezeBrush("#090D16");
                CardBorderBrush = FreezeBrush("#78350F");
                NumberBadgeBg = FreezeBrush("#78350F");
                NumberBadgeFg = FreezeBrush("#FDE68A");
                break;
        }

        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusBg));
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(NumberBadgeBg));
        OnPropertyChanged(nameof(NumberBadgeFg));
    }

    private static SolidColorBrush FreezeBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
