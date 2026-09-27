namespace UsageMonitorWpf.ViewModels;

// A ComboBox/menu choice whose stored value stays stable while its label follows the UI language.
public sealed class OptionItem(object value, Func<string> label) : ObservableObject
{
    public object Value { get; } = value;
    public string Label => label();
    public void Refresh() => OnPropertyChanged(nameof(Label));
    public override string ToString() => Label;
}
