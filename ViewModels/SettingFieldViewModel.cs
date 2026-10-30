using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using System.Reflection;
using OculusGoAirMouse.Models;

namespace OculusGoAirMouse.ViewModels;

public partial class SettingFieldViewModel : ObservableObject
{
    private readonly PropertyInfo property;
    public string Label { get; }
    public bool IsBoolean => property.PropertyType == typeof(bool);
    [ObservableProperty] private string textValue = "";
    [ObservableProperty] private bool booleanValue;

    public SettingFieldViewModel(string name, string label, Settings settings)
    {
        property = typeof(Settings).GetProperty(name) ?? throw new ArgumentException(name);
        Label = label;
        if (IsBoolean) BooleanValue = (bool)property.GetValue(settings)!;
        else TextValue = Convert.ToString(property.GetValue(settings), CultureInfo.CurrentCulture) ?? "";
    }
    public void WriteTo(Settings settings)
    {
        try
        {
            object value;
            if (IsBoolean) value = BooleanValue;
            else if (property.PropertyType == typeof(string)) value = TextValue.Trim();
            else if (property.PropertyType == typeof(int)) value = int.Parse(TextValue.Trim(), CultureInfo.CurrentCulture);
            else value = double.Parse(TextValue.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture);
            property.SetValue(settings, value);
        }
        catch (FormatException) { throw new ArgumentException($"{Label}：请输入有效的数值。"); }
        catch (OverflowException) { throw new ArgumentException($"{Label}：数值过大。"); }
    }
}
