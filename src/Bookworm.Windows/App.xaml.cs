using System.Windows;
using Bookworm.Core.Auth;
using Bookworm.Core.Brain;
using Bookworm.Core.Brain.Tools;
using Bookworm.Core.Library;
using Bookworm.Core.Memory;
using Bookworm.Core.Orchestration;
using Bookworm.Core.Speech;
using Bookworm.Windows.Platform.Credentials;
using Bookworm.Windows.Platform.Speech;
using Bookworm.Windows.Services;
using Bookworm.Windows.ViewModels;

namespace Bookworm.Windows;

/// <summary>
/// Composition root. All credentials (VA login, Anthropic key, Azure Speech, Azure memory store) are
/// read from Windows Credential Manager — the same store Bookworm.Console's setup commands write to,
/// so anything already configured there (as it was during Phase 1–3 testing) just works here too.
/// No first-run setup UI yet: that's Phase 5 polish. For now, missing configuration surfaces as a
/// status message rather than a crash.
/// </summary>
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var credentialStore = new WindowsCredentialManagerStore();

        var speechCredentials = await AzureSpeechCredentials.LoadAsync(credentialStore);
        if (speechCredentials is null)
        {
            MessageBox.Show(
                "No Azure Speech credentials found. Run Bookworm.Console's 'azurespeechsetup' command first, then relaunch.",
                "Bookworm", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var vaClient = new VaSessionManager(new VaLibraryClient(), credentialStore);

        var apiKey = await credentialStore.GetSecretAsync(CredentialKeys.AnthropicApiKey);
        IBrain brain = apiKey is not null ? new ClaudeBrain(apiKey) : new StubBrain();

        var memoryCredentials = await AzureMemoryStoreCredentials.LoadAsync(credentialStore);
        IMemoryStore memoryStore = memoryCredentials is not null
            ? new AzureBlobMemoryStore(memoryCredentials.ConnectionString, memoryCredentials.ContainerName)
            : new InMemoryMemoryStore();

        var toolExecutor = new ToolCallExecutor(vaClient);
        var orchestrator = new LibrarianOrchestrator(brain, toolExecutor, vaClient, memoryStore);

        ISpeechRecognizer recognizer = new AzureSpeechRecognizer(speechCredentials);
        ISpeechSynthesizer synthesizer = new SapiSpeechSynthesizer();
        var controller = new PushToTalkController(recognizer, synthesizer, orchestrator);

        var viewModel = new MainViewModel();
        var mainWindow = new MainWindow(viewModel, controller);
        mainWindow.Show();

        viewModel.StatusText = "Initializing — loading your bookshelf and reading profile…";
        try
        {
            await orchestrator.InitializeAsync();
            viewModel.StatusText = "Ready. Press and hold Talk, or press Space or Enter, to speak.";
        }
        catch (Exception ex)
        {
            viewModel.StatusText = $"Couldn't start up: {ex.Message}";
        }
    }
}
