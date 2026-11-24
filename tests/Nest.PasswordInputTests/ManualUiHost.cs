using Caliburn.Micro;
using Nest.Views.Admin;
using Nest.Views.Authentication;
using PasswordInputTests.Ui.Admin;
using PasswordInputTests.Ui.Authentication;
using PasswordInputTests.Ui.Shell;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PasswordInputTests;

// Test-only visible host. Never loads App.xaml, Bootstrapper or production navigation.
internal sealed class ManualUiHost : Window
{
    private readonly BenutzerViewModel admin = new();
    private readonly LoginViewModel login = new(new ShellViewModel()) { UserName = "offline-login" };
    private readonly BenutzerView adminView = new();
    private readonly LoginView loginView = new();
    private readonly TextBlock notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    private readonly TabControl tabs = new();
    private readonly CheckBox saveSuccess = new() { Content = "Fake Add/Update: Success (unchecked = Refused)", IsChecked = true, Margin = new Thickness(8) };
    private readonly CheckBox loginSuccess = new() { Content = "Fake Login: Success (no credential verification or real navigation)", Margin = new Thickness(8) };
    private bool cleaned;

    public ManualUiHost()
    {
        VerifyIsolation();
        Title = "OFFLINE — синтетические данные | Password UI";
        Width = 1200; Height = 850; MinWidth = 650; MinHeight = 500;
        var root = new DockPanel();
        var header = new StackPanel { Background = Brushes.LightYellow };
        header.Children.Add(new TextBlock { Text = "OFFLINE — синтетические данные. No MariaDB. Use invented passwords only.",
            FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) });
        header.Children.Add(new TextBlock { Text = "Add/Update only simulate acknowledgement; no persistent storage. Delete is disabled. Login only records fake navigation.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 8, 0) });
        header.Children.Add(saveSuccess); header.Children.Add(loginSuccess);
        saveSuccess.Checked += SaveModeChanged; saveSuccess.Unchecked += SaveModeChanged;
        loginSuccess.Checked += LoginModeChanged; loginSuccess.Unchecked += LoginModeChanged;
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        DockPanel.SetDock(notice, Dock.Bottom); root.Children.Add(notice);
        ViewModelBinder.Bind(login, loginView, null);
        ViewModelBinder.Bind(admin, adminView, null);
        admin.SelectedUser = admin.Users[0];
        tabs.Items.Add(new TabItem { Header = "Login", Content = loginView });
        tabs.Items.Add(new TabItem { Header = "Benutzerverwaltung", Content = adminView });
        root.Children.Add(tabs); Content = root;
        State.Notice = ShowNotice;
        SaveModeChanged(this, new RoutedEventArgs()); LoginModeChanged(this, new RoutedEventArgs());
        Closed += (_, _) => Cleanup();
    }

    private void ShowNotice(string message) => notice.Text = message;
    private void SaveModeChanged(object sender, RoutedEventArgs e) =>
        State.AddSuccess = State.UpdateSuccess = saveSuccess.IsChecked == true;
    private void LoginModeChanged(object sender, RoutedEventArgs e) => State.LoginResult = loginSuccess.IsChecked == true
        ? new Nest.Models.User("offline-login", "Offline Login", "Admin", "Aktiv") : null;

    internal void Cleanup()
    {
        if (cleaned) return;
        cleaned = true;
        admin.Password = ""; login.Password = "";
        adminView.DataContext = null; loginView.DataContext = null;
        saveSuccess.Checked -= SaveModeChanged; saveSuccess.Unchecked -= SaveModeChanged;
        loginSuccess.Checked -= LoginModeChanged; loginSuccess.Unchecked -= LoginModeChanged;
        foreach (TabItem tab in tabs.Items) tab.Content = null;
        tabs.Items.Clear(); Content = null; notice.Text = "";
        State.Reset();
    }

    internal static void VerifyIsolation()
    {
        var assembly = typeof(ManualUiHost).Assembly;
        foreach (var vm in new[] { typeof(BenutzerViewModel), typeof(LoginViewModel) })
        {
            var repository = vm.GetField("_userRepository", BindingFlags.NonPublic | BindingFlags.Instance);
            if (vm.Assembly != assembly || repository?.FieldType != typeof(Data.UserRepository) ||
                repository.FieldType.Assembly != assembly)
                throw new InvalidOperationException("Offline Repository substitution not verified.");
        }
        if (typeof(LoginViewModel).GetField("_shell", BindingFlags.NonPublic | BindingFlags.Instance)?.FieldType != typeof(ShellViewModel))
            throw new InvalidOperationException("Offline Shell substitution not verified.");
        bool rejected = false;
        try { _ = Nest.Data.IntegrationConnectionSettings.FromEnvironment(); }
        catch (InvalidOperationException) { rejected = true; }
        if (!rejected || Environment.GetEnvironmentVariable(Nest.Data.IntegrationConnectionSettings.ModeVariable) != "invalid-offline-password-test")
            throw new InvalidOperationException("Offline database configuration guard not verified.");
    }

    // Construct real compiled Views without showing the manual window.
    internal static void OfflineChecks()
    {
        int checks = 0;
        void Check(bool value) { if (!value) throw new InvalidOperationException("Manual host invariant failed."); checks++; }
        State.Reset(); State.ManualMode = true;
        var host = new ManualUiHost();
        try
        {
            Check(host.tabs.Items.Count == 2 && host.admin.Users.Count >= 2);
            Check(host.adminView.DataContext == host.admin && host.loginView.DataContext == host.login);
            host.saveSuccess.IsChecked = false; Check(!State.AddSuccess && !State.UpdateSuccess);
            host.admin.Password = "synthetic-only"; host.admin.Aktualisieren(); Check(host.admin.Password == "synthetic-only");
            host.admin.SelectedUser = host.admin.Users[1]; Check(host.admin.Password == "");
            host.saveSuccess.IsChecked = true; Check(State.AddSuccess && State.UpdateSuccess);
            host.admin.Password = "synthetic-only"; host.admin.Aktualisieren(); Check(host.admin.Password == "");
            Check(State.Request == null && State.SubmittedPassword == null && Dialog.Messages.Count == 0);
            host.loginSuccess.IsChecked = true; host.login.Password = "synthetic-only"; host.login.Login();
            Check(State.Destination == "Admin" && State.LoginPassword == null && State.LoginName == null);
            host.loginSuccess.IsChecked = false; Check(State.LoginResult == null);
            Check(!host.IsVisible); // Automatic mode never opens the interactive host.
        }
        finally { host.Cleanup(); host.Close(); }
        Check(host.tabs.Items.Count == 0 && host.adminView.DataContext == null && host.loginView.DataContext == null);
        Check(State.Notice == null && !State.ManualMode && State.LoginResult == null);
        Console.WriteLine($"PASS: {checks} manual-host offline assertions; interactive window not shown.");
    }
}
