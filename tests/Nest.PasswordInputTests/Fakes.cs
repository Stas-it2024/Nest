using Nest.Models;
using System.Windows;

namespace PasswordInputTests
{
    internal static class State
    {
        internal static bool ManualMode;
        internal static Action<string>? Notice;
        internal static bool AddSuccess, UpdateSuccess, FailReload;
        internal static Exception? WriteError;
        internal static Action? DuringWrite;
        internal static User? Request;
        internal static string? SubmittedPassword, LoginPassword, LoginName;
        internal static User? LoginResult;
        internal static int AddCalls, UpdateCalls, LoginCalls, NavigationCalls;
        internal static string Destination = "";
        internal static readonly User[] Rows = { new("offline-a", "Offline A", "Admin", "Aktiv") { Id = 101 },
            new("offline-b", "Offline B", "Rezeptionist", "Aktiv") { Id = 102 } };
        internal static void Reset()
        {
            ManualMode = false; Notice = null;
            AddSuccess = UpdateSuccess = true; FailReload = false; WriteError = null; DuringWrite = null;
            Request = null; SubmittedPassword = LoginPassword = LoginName = null; LoginResult = null;
            AddCalls = UpdateCalls = LoginCalls = NavigationCalls = 0; Destination = ""; Dialog.Messages.Clear();
        }
    }
    internal static class Dialog
    {
        internal static readonly List<string> Messages = new();
        public static MessageBoxResult Show(string text, string title = "", MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None)
        {
            if (State.ManualMode)
            {
                State.Notice?.Invoke("OFFLINE: " + text);
                // Destructive actions are outside the password UI verification scope.
                return buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
            }
            Messages.Add(text); return MessageBoxResult.Yes;
        }
    }
}
namespace PasswordInputTests.Data
{
    // A recording boundary double, not a rewritten SQL/storage algorithm.
    public sealed class UserRepository
    {
        public List<User> GetAll()
        {
            if (State.FailReload) throw new InvalidOperationException("synthetic reload failure");
            return State.Rows.Select(u => new User(u.UserName, u.FullName, u.Role, u.Status) { Id = u.Id }).ToList();
        }
        public bool Add(User user)
        {
            State.AddCalls++; Record(user); return State.AddSuccess;
        }
        public bool Update(User user)
        {
            State.UpdateCalls++; Record(user); return State.UpdateSuccess;
        }
        private static void Record(User user)
        {
            if (State.ManualMode)
            {
                State.Notice?.Invoke("OFFLINE: fake Add/Update; result = " +
                    ((State.AddCalls > 0 && State.UpdateCalls == 0 ? State.AddSuccess : State.UpdateSuccess) ? "Success" : "Refused"));
                return;
            }
            State.Request = user; State.SubmittedPassword = user.Password; State.DuringWrite?.Invoke();
            if (State.WriteError != null) throw State.WriteError;
        }
        public void Delete(int id) => throw new InvalidOperationException("Delete is outside this offline test scope.");
        public User? Login(string? name, string? password)
        {
            State.LoginCalls++;
            if (!State.ManualMode) { State.LoginName = name; State.LoginPassword = password; }
            return State.LoginResult;
        }
    }
}
namespace PasswordInputTests.Fakes
{
    public sealed class MySqlException(int number) : Exception { public int Number => number; }
}
namespace PasswordInputTests.Ui.Shell
{
    public sealed class ShellViewModel
    {
        public string WindowTitle { get; set; } = "";
        private static void Navigate(string name) { State.NavigationCalls++; State.Destination = name; State.Notice?.Invoke("OFFLINE: fake navigation to " + name + "; no real Shell opened."); }
        public void ShowAdmin() => Navigate("Admin");
        public void ShowReception() => Navigate("Rezeptionist");
        public void ShowCustomer() => Navigate("Customer");
    }
}
