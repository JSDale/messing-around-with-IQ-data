using System.Collections.ObjectModel;

namespace HackRfIqDataTest.ViewModels;

public class UserLog
{
    public ObservableCollection<string> Log { get; } = [];

    public void Clear()
    {
        this.Log.Clear();
    }
}