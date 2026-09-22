using System.Windows;
using Bookworm.Core.Auth;
using Bookworm.Core.Brain;
using Bookworm.Core.Brain.Tools;
using Bookworm.Core.Library;
using Bookworm.Core.Logging;
using Bookworm.Core.Memory;
using Bookworm.Core.Orchestration;
using Bookworm.Core.Speech;
using Bookworm.Windows.Platform.Credentials;
using Bookworm.Windows.Platform.Speech;
using Bookworm.Windows.Services;
using Bookworm.Windows.ViewModels;

namespace Bookworm.Windows;

/// <summary>
/// Composition root. This app is built for one specific end user (not a general public download): the
/// developer's own Anthropic, Azure Speech, and Azure Storage credentials are seeded into Windows
/// Credential Manager by the installer at install time (see installer/README.md) — the end user never
/// sees or enters them. The only thing the end user provides is their own Vision Australia Library
/// login, via <see cref="SetupWindow"/> on first run.
/// </summary>
public partial class App : Application
{
    private IAppLogger _logger = new FileAppLogger();

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Without Handled = true, any unexpected exception (async-void event handlers like the global
        // hotkey's callback are especially prone to this) silently kills the whole app — bad for anyone,
        // worse for a vision-impaired user with no visible crash dialog to explain what just happened.
        // Logging and swallowing keeps the app alive so the next Talk press can just work.
        DispatcherUnhandledException += (_, args) =>
        {
            _logger.Error("Unhandled UI exception — recovered, app stays open.", args.Exception);
            args.Handled = true;
        };

        _logger.Info("Bookworm starting.");
        var credentialStore = new WindowsCredentialManagerStore();

        if (await LibraryCredentials.LoadAsync(credentialStore) is null)
        {
            _logger.Info("No VA credentials found — showing first-run setup.");
            var setup = new SetupWindow(credentialStore);
            if (setup.ShowDialog() != true)
            {
                _logger.Info("Setup cancelled by user — exiting.");
                Shutdown();
                return;
            }
        }

        var speechCredentials = await AzureSpeechCredentials.LoadAsync(credentialStore);
        if (speechCredentials is null)
        {
            _logger.Error("Azure Speech credentials missing — this should have been seeded by the installer.");
            MessageBox.Show(
                "Bookworm isn't fully installed correctly — its speech service isn't configured. Please reinstall, or contact whoever set this up for you.",
                "Bookworm", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
            return;
        }

        var vaClient = new VaSessionManager(new VaLibraryClient(), credentialStore);

        var apiKey = await credentialStore.GetSecretAsync(CredentialKeys.AnthropicApiKey);
        IBrain brain = apiKey is not null ? new ClaudeBrain(apiKey) : new StubBrain();
        if (apiKey is null)
        {
            _logger.Warn("No Anthropic API key found — falling back to StubBrain (canned responses).");
        }

        var memoryCredentials = await AzureMemoryStoreCredentials.LoadAsync(credentialStore);
        IMemoryStore memoryStore = memoryCredentials is not null
            ? new AzureBlobMemoryStore(memoryCredentials.ConnectionString, memoryCredentials.ContainerName)
            : new InMemoryMemoryStore();
        if (memoryCredentials is null)
        {
            _logger.Warn("No Azure memory-store credentials found — preferences won't persist across sessions.");
        }

        var toolExecutor = new ToolCallExecutor(vaClient);
        var orchestrator = new LibrarianOrchestrator(brain, toolExecutor, vaClient, memoryStore);

        ISpeechRecognizer recognizer = new AzureSpeechRecognizer(speechCredentials);
        ISpeechSynthesizer synthesizer = new SapiSpeechSynthesizer();
        var controller = new PushToTalkController(recognizer, synthesizer, orchestrator, _logger);

        var viewModel = new MainViewModel();
        var mainWindow = new MainWindow(viewModel, controller);
        mainWindow.Show();

        viewModel.StatusText = "Initializing — loading your bookshelf and reading profile…";
        try
        {
            await orchestrator.InitializeAsync();
            viewModel.StatusText = "Ready. Press and hold Talk, or press Space or Enter, to speak.";
            _logger.Info("Startup complete — ready.");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed during orchestrator initialization.", ex);
            viewModel.StatusText = $"Couldn't start up: {ex.Message}";
        }
    }
}
