using Caliburn.Micro;
using Nest.Views.Admin;
using Nest.Views.Authentication;
using Nest.Views.Behaviors;
using PasswordInputTests.Ui.Admin;
using PasswordInputTests.Ui.Authentication;
using PasswordInputTests.Ui.Shell;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

namespace PasswordInputTests;

internal static class Program
{
    private static int checks;
    private const string First = "Synthetic-input-A Ω";
    private const string Second = "Synthetic-input-B !";
    private static void Check(bool condition, string name)
    { if (!condition) throw new InvalidOperationException("Password input invariant failed: " + name); checks++; }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 0 && !(args.Length == 1 && args[0] == "--manual-ui"))
        {
            Console.Error.WriteLine("Usage: Nest.PasswordInputTests [--manual-ui]");
            return 2;
        }
        var oldMode = Environment.GetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable);
        var oldConnection = Environment.GetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable);
        try
        {
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable, "invalid-offline-password-test");
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable, null);
            ManualUiHost.VerifyIsolation();
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            foreach (var name in new[] { "Palette", "CommonStyles", "ReceptionFormStyles", "DataGridStyles", "ButtonStyles" })
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri("pack://application:,,,/Nest;component/Styles/" + name + ".xaml") });
            if (args.Length == 1)
            {
                State.Reset();
                State.ManualMode = true;
                Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
                return Application.Current.Run(new ManualUiHost());
            }
            ModelChecks(); BridgeChecks(); AdminViewChecks(); LoginViewChecks();
            Console.WriteLine($"PASS: {checks} password-input offline assertions; current ViewModel bodies and compiled Views; recording Repository/Shell/dialog doubles; no database.");
            ManualUiHost.OfflineChecks();
            return 0;
        }
        catch (Exception ex)
        {
            // Do not dump password values, models or arbitrary Repository/driver exceptions.
            Console.Error.WriteLine("FAIL: password-input offline checks (" + ex.GetType().Name + ")."); return 1;
        }
        finally
        {
            State.Reset();
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable, oldMode);
            Environment.SetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ConnectionVariable, oldConnection);
        }
    }
    private static BenutzerViewModel Admin(bool selected = true)
    {
        var vm = new BenutzerViewModel();
        if (selected) vm.SelectedUser = vm.Users[0];
        else { vm.UserName = "offline-new"; vm.FullName = "Offline new"; vm.Role = "Rezeptionist"; vm.Status = "Aktiv"; }
        return vm;
    }
    private static void ModelChecks()
    {
        State.Reset(); var vm = Admin(); vm.Password = First;
        var first = vm.SelectedUser; vm.SelectedUser = first;
        Check(vm.Password == First, "same selection retains draft");
        vm.SelectedUser = vm.Users[1]; Check(vm.Password == "", "changing user clears draft");
        vm.Password = First; vm.SelectedUser = null; Check(vm.Password == "", "deselect clears draft");
        foreach (bool add in new[] { false, true })
        foreach (bool success in new[] { false, true })
        foreach (bool technical in new[] { false, true })
        {
            State.Reset(); vm = Admin(!add); vm.Password = First;
            State.AddSuccess = State.UpdateSuccess = success;
            if (technical) State.WriteError = new InvalidOperationException("synthetic failure");
            State.DuringWrite = () => Check(vm.Password == First, "draft retained until acknowledged write");
            try { if (add) vm.Hinzufügen(); else vm.Aktualisieren(); }
            catch (InvalidOperationException) when (add && technical) { }
            Check(vm.Password == (success && !technical ? "" : First), "clear only on success");
            Check(State.SubmittedPassword == First && State.Request?.Password == null, "transient request cleared, submitted input unchanged");
            Check(vm.Users.All(u => u.Password == null), "user list does not retain submitted password");
            Check(State.AddCalls + State.UpdateCalls == 1, "no write retry");
            Check(Dialog.Messages.All(m => !m.Contains(First)), "no password echo");
        }
        State.Reset(); vm = Admin(); vm.Password = First; State.UpdateSuccess = false; vm.Aktualisieren();
        Check(vm.Password == First, "failure retains current draft");
        vm.SelectedUser = vm.Users[1]; Check(vm.Password == "", "failure then user change clears draft");
        State.UpdateSuccess = true; vm.Aktualisieren();
        Check(State.Request?.Id == 102 && string.IsNullOrEmpty(State.SubmittedPassword), "other user receives no password replacement");
        State.Reset(); vm = Admin(); vm.Password = First; State.FailReload = true; vm.Aktualisieren();
        Check(vm.Password == "" && State.Request?.Password == null, "confirmed update followed by reload failure clears draft");
        State.Reset(); vm = Admin(); vm.Password = First; State.WriteError = new Fakes.MySqlException(1062); vm.Aktualisieren();
        Check(vm.Password == First && State.Request?.Password == null, "duplicate refusal retains form but not request");
        State.Reset(); vm = Admin(); vm.Password = First; vm.UserName = null; vm.Aktualisieren();
        Check(vm.Password == First && State.UpdateCalls == 0, "validation refusal preserves draft");
        vm.ClearForm(); Check(vm.Password == "", "explicit clear");
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static Window Host(UserControl view, object vm)
    {
        ViewModelBinder.Bind(vm, view, null);
        var window = new Window { Content = view, Width = 1000, Height = 720, ShowActivated = false, ShowInTaskbar = false, Opacity = 0 };
        window.Show(); Pump(); return window;
    }
    private static void BridgeChecks()
    {
        State.Reset(); var vm = Admin(); var box = new PasswordBox(); PasswordDraftBinding.SetIsEnabled(box, true);
        BindingOperations.SetBinding(box, PasswordDraftBinding.ValueProperty, new Binding("Password")
        { Source = vm, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        foreach (string input in new[] { First, Second, "", "  preserved spaces  ", "äΩ漢字", new string('x', 256) })
        {
            box.Password = input; Pump(); Check(vm.Password == input, "exact PasswordBox to model");
            vm.Password = Second; Pump(); Check(box.Password == Second, "exact model to PasswordBox");
            Check(BindingOperations.IsDataBound(box, PasswordDraftBinding.ValueProperty), "binding retained");
        }
        vm.Password = null; Pump(); Check(box.Password == "", "null clears control");
        PasswordDraftBinding.SetIsEnabled(box, false); box.Password = First; Check(vm.Password == null, "disable detaches handler");
        PasswordDraftBinding.SetIsEnabled(box, true); Pump(); Check(box.Password == "", "reenable resyncs without stale writeback");
        BindingOperations.ClearBinding(box, PasswordDraftBinding.ValueProperty);
    }
    private static void AdminViewChecks()
    {
        State.Reset(); var vm = Admin(); var view = new BenutzerView(); var window = Host(view, vm);
        try
        {
            var box = (PasswordBox)view.FindName("PasswordDraftBox");
            Check(box != null && PasswordDraftBinding.GetIsEnabled(box), "compiled admin masked control");
            box!.Password = First; Pump(); Check(vm.Password == First, "compiled admin input");
            vm.SelectedUser = vm.Users[1]; Pump(); Check(vm.Password == "" && box.Password == "", "selection clears VM and control");
            box.Password = Second; State.UpdateSuccess = false; vm.Aktualisieren(); Pump();
            Check(box.Password == Second && vm.Password == Second, "failed save retains masked draft");
            vm.SelectedUser = vm.Users[0]; Pump(); Check(box.Password == "", "failed draft not transferred");
            box.Password = First; State.UpdateSuccess = true; vm.Aktualisieren(); Pump();
            Check(vm.Password == "" && box.Password == "", "success clears compiled control");
            box.Password = First; vm.ClearForm(); Pump(); Check(box.Password == "", "clear command clears compiled control");
            var other = Admin(); other.Password = Second; view.DataContext = other; Pump();
            Check(box.Password == Second && vm.Password == "", "new DataContext has its own draft");
            var creating = Admin(selected: false); view.DataContext = creating; Pump();
            box.Password = First; State.AddSuccess = false; creating.Hinzufügen(); Pump();
            Check(box.Password == First && creating.Password == First, "duplicate create keeps masked draft");
            State.AddSuccess = true; creating.Hinzufügen(); Pump();
            Check(box.Password == "" && creating.Password == "" && creating.Users.Last().Password == null, "compiled create success clears input and list record");
            view.DataContext = null; Pump(); Check(box.Password == "", "detachment clears stale display");
        }
        finally { window.Close(); Pump(); }
    }
    private static void LoginViewChecks()
    {
        State.Reset(); var vm = new LoginViewModel(new ShellViewModel()) { UserName = "offline-login" };
        var view = new LoginView(); var window = Host(view, vm);
        try
        {
            var hidden = (PasswordBox)view.FindName("PasswordBox"); var visible = (TextBox)view.FindName("VisiblePasswordBox");
            hidden.Password = First; Pump();
            Check(vm.Password == First && visible.Text == First, "masked input syncs both representations");
            for (int i = 0; i < 8; i++)
            {
                vm.ShowPassword = true; Pump();
                Check(visible.Visibility == Visibility.Visible && hidden.Visibility == Visibility.Collapsed, "visible mode");
                visible.Text = i % 2 == 0 ? Second : First; Pump(); var expected = visible.Text;
                Check(vm.Password == expected && hidden.Password == expected, "visible edit reaches masked input");
                vm.ShowPassword = false; Pump();
                Check(hidden.Visibility == Visibility.Visible && visible.Visibility == Visibility.Collapsed && hidden.Password == expected && vm.Password == expected, "hiding does not revert input");
                vm.Login(); Check(State.LoginPassword == expected && State.LoginName == "offline-login", "existing Login contract verifies displayed draft");
            }
            hidden.Password = ""; Pump(); Check(vm.Password == "" && visible.Text == "", "empty edit syncs");
            visible.Text = Second; Pump();
            ((Button)view.FindName("Login")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump();
            Check(State.LoginPassword == Second, "compiled Login action uses current input");
            vm.Password = null; Pump(); Check(hidden.Password == "" && visible.Text == "", "model reset clears both controls");
            hidden.Password = First; vm.SetPassword(hidden); Check(vm.Password == First, "legacy SetPassword contract retained");
            foreach (string role in new[] { "Admin", "Rezeptionist" })
            {
                State.LoginResult = new Nest.Models.User("offline-login", "Offline", role, "Aktiv");
                vm.Login(); Check(State.Destination == role, "existing role routing unchanged");
            }
            view.DataContext = null; Pump(); Check(hidden.Password == "" && visible.Text == "", "login detachment clears visual draft");
        }
        finally { window.Close(); Pump(); }
    }
}
