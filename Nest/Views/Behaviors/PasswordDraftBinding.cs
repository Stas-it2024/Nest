using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Nest.Views.Behaviors;

// UI-only bridge for the existing string draft contract. No cache, logging or external subscriptions.
// PasswordBox.Password is not a DependencyProperty; both directions need explicit synchronization.
public static class PasswordDraftBinding
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PasswordDraftBinding), new PropertyMetadata(false, EnabledChanged));
    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(string), typeof(PasswordDraftBinding),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValueChanged));
    private static readonly DependencyProperty UpdatingProperty = DependencyProperty.RegisterAttached(
        "Updating", typeof(bool), typeof(PasswordDraftBinding), new PropertyMetadata(false));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static string? GetValue(DependencyObject target) => (string?)target.GetValue(ValueProperty);
    public static void SetValue(DependencyObject target, string? value) => target.SetValue(ValueProperty, value);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not PasswordBox box) return;
        box.PasswordChanged -= PasswordChanged;
        if ((bool)args.NewValue) { box.PasswordChanged += PasswordChanged; Apply(box); }
    }
    private static void ValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is PasswordBox box && GetIsEnabled(box)) Apply(box);
    }
    private static void Apply(PasswordBox box)
    {
        if ((bool)box.GetValue(UpdatingProperty)) return;
        var value = GetValue(box) ?? string.Empty;
        if (box.Password == value) return; // Do not reset caret/input for an unchanged draft.
        box.SetValue(UpdatingProperty, true);
        try { box.Password = value; }
        finally { box.SetValue(UpdatingProperty, false); }
    }
    private static void PasswordChanged(object sender, RoutedEventArgs args)
    {
        var box = (PasswordBox)sender;
        if ((bool)box.GetValue(UpdatingProperty)) return;
        box.SetValue(UpdatingProperty, true);
        try
        {
            box.SetCurrentValue(ValueProperty, box.Password); // Preserve the BindingExpression.
            BindingOperations.GetBindingExpression(box, ValueProperty)?.UpdateSource();
        }
        finally { box.SetValue(UpdatingProperty, false); }
    }
}
