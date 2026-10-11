using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace HackRfIqDataTest.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly UserLog _userLog;
    public double StartFrequency { get; set => _ = this.SetField(ref field, value); }
    
    public double EndFrequency { get; set => _ = this.SetField(ref field, value); }
    
    public ICommand CaptureData { get; }

    public ObservableCollection<string> Log => this._userLog.Log;
    
    public ICommand ClearLog { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(UserLog userLog)
    {
        this._userLog = userLog;
        this.CaptureData = new RelayCommand(this.OnCaptureDataRequested);
        this.ClearLog = new RelayCommand(this.OnClearLog);
    }

    private void OnClearLog()
    {
        this._userLog.Clear();
    }

    private void OnCaptureDataRequested()
    {
        this._userLog.Log.Add("Attempting to get data...");
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        this.OnPropertyChanged(propertyName);
        return true;
    }
}