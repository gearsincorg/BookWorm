using System.Windows;
using Bookworm.Core.Auth;
using Bookworm.Core.Library;

namespace Bookworm.Windows;

/// <summary>
/// First-run setup for the end user (not the developer): just their own Vision Australia Library login.
/// Everything else (Anthropic key, Azure Speech, Azure memory storage) belongs to the developer's own
/// accounts and is seeded into Credential Manager by the installer at install time — see
/// installer/README.md and docs/decisions.md's Phase 5 section. It is never entered here and never
/// committed to source control.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly ICredentialStore _credentialStore;

    public SetupWindow(ICredentialStore credentialStore)
    {
        InitializeComponent();
        _credentialStore = credentialStore;
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        var email = VaEmailBox.Text.Trim();
        var password = VaPasswordBox.Password.Trim();

        if (email.Length == 0 || password.Length == 0)
        {
            StatusText.Text = "Please enter both your email/member number and password.";
            return;
        }

        SignInButton.IsEnabled = false;
        StatusText.Text = "Signing in…";
        try
        {
            using var client = new VaLibraryClient();
            var credentials = new LibraryCredentials { EmailOrVaid = email, Password = password };
            await client.AuthenticateAsync(credentials);
            await credentials.SaveAsync(_credentialStore);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Sign in failed: {ex.Message}. Please check your email and password and try again.";
        }
        finally
        {
            SignInButton.IsEnabled = true;
        }
    }
}
