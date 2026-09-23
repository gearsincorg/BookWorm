using Azure.Storage.Blobs;
using Bookworm.Core.Auth;
using Bookworm.Core.Brain;
using Bookworm.Core.Brain.Tools;
using Bookworm.Core.Library;
using Bookworm.Core.Library.Exceptions;
using Bookworm.Core.Library.Models;
using Bookworm.Core.Memory;
using Bookworm.Core.Orchestration;
using Bookworm.Core.Speech;
using Bookworm.Windows.Platform.Credentials;
using Bookworm.Windows.Platform.Speech;

var credentialStore = new WindowsCredentialManagerStore();
var rawClient = new VaLibraryClient();
IVaLibraryClient client = new VaSessionManager(rawClient, credentialStore);

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "login":
            await LoginAsync();
            break;
        case "search":
            await SearchAsync(args.ElementAtOrDefault(1) ?? Prompt("Keyword"), args.ElementAtOrDefault(2));
            break;
        case "bookshelf":
            await ShowBookshelfAsync();
            break;
        case "add":
            await AddToBookshelfAsync(Require(args, 1, "bookshareId"), Require(args, 2, "format"));
            break;
        case "remove":
            await RemoveFromBookshelfAsync(Require(args, 1, "activeTitleId"), args.ElementAtOrDefault(2));
            break;
        case "requestlist":
            await ShowRequestListAsync();
            break;
        case "addrequest":
            await AddToRequestListAsync(Require(args, 1, "bookshareId"));
            break;
        case "subscriptions":
            await ShowSubscriptionsAsync();
            break;
        case "subscribe":
            await SubscribeAsync(Require(args, 1, "bookshareId"));
            break;
        case "history":
            await ShowHistoryAsync();
            break;
        case "raw":
            await ShowRawAsync(Require(args, 1, "relativeUrl"));
            break;
        case "speechtest":
            await SpeechEchoTestAsync(new SystemSpeechRecognizer());
            break;
        case "speechtest2":
            await SpeechEchoTestAsync(new WinRtSpeechRecognizer());
            break;
        case "azurespeechsetup":
            await AzureSpeechSetupAsync();
            break;
        case "speechtest3":
            await SpeechEchoTestAsync(await CreateAzureRecognizerAsync());
            break;
        case "say":
            await SayAsync(string.Join(' ', args.Skip(1)));
            break;
        case "claudesetup":
            await ClaudeSetupAsync();
            break;
        case "memorysetup":
            await MemorySetupAsync();
            break;
        case "listcontainers":
            await ListContainersAsync();
            break;
        case "chat":
            await ChatAsync(dryRun: args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase));
            break;
        case "seedsecrets":
            await SeedSecretsAsync(args);
            break;
        case "exportsecretsforinstaller":
            await ExportSecretsForInstallerAsync(args.ElementAtOrDefault(1) ?? "installer/secrets.local.iss");
            break;
        default:
            PrintUsage();
            return 1;
    }
    return 0;
}
catch (VaAuthenticationException ex)
{
    Console.Error.WriteLine($"Authentication failed: {ex.Message}");
    return 1;
}
catch (VaLoanCapExceededException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
catch (VaRequestFailedException ex)
{
    Console.Error.WriteLine($"Request failed: {ex.Message}");
    if (ex.ResponseBody is not null)
    {
        Console.Error.WriteLine($"Response body: {ex.ResponseBody}");
    }
    return 1;
}

static string Require(string[] args, int index, string name) =>
    args.ElementAtOrDefault(index) ?? throw new ArgumentException($"Missing required argument: {name}");

static string Prompt(string label)
{
    Console.Write($"{label}: ");
    return Console.ReadLine() ?? "";
}

static string PromptHidden(string label)
{
    Console.Write($"{label}: ");
    var sb = new System.Text.StringBuilder();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace)
        {
            if (sb.Length > 0)
            {
                sb.Length--;
                Console.Write("\b \b");
            }
            continue;
        }
        if (!char.IsControl(key.KeyChar))
        {
            sb.Append(key.KeyChar);
            Console.Write('*');
        }
    }
    Console.WriteLine();
    // Trimmed defensively: a pasted key/connection string with a trailing newline or stray space is a
    // common paste artifact and produces a confusing 401 rather than an obvious error.
    return sb.ToString().Trim();
}

static void PrintUsage()
{
    Console.WriteLine("""
        Bookworm.Console — Vision Australia Library API test harness

        Usage:
          login                                Log in and save credentials to Windows Credential Manager
          search <keyword> [format]            Quick search (format e.g. DAISY_Audio_Human)
          bookshelf                            Show current bookshelf
          add <bookshareId> <format>           Add a title to the bookshelf
          remove <activeTitleId> [type]        Remove a title from the bookshelf (type: book|music|periodical)
          requestlist                          Show the request list
          addrequest <bookshareId>             Add a title to the request list
          subscriptions                        Show periodical subscriptions
          subscribe <bookshareId>              Subscribe to a periodical
          history                              Show loan history
          raw <relativeUrl>                    Debug: print the raw response body for a GET (e.g. /library/my-history)
          speechtest                           Push-to-talk echo test using legacy SAPI dictation
          speechtest2                          Push-to-talk echo test using modern WinRT speech recognition
          azurespeechsetup                     Enter and save your Azure AI Speech key + region
          speechtest3                          Push-to-talk echo test using Azure AI Speech
          say <text>                           Speak text via TTS (no mic needed) — quick TTS-only sanity check
          claudesetup                          Enter and save your Anthropic API key
          memorysetup                          Enter and save your Azure Storage connection string + container
          listcontainers                       Debug: list containers in the configured Azure Storage account
          chat [--dry-run]                     Typed conversational Brain harness (Phase 3) — the real thing
          seedsecrets --claude-key=... --speech-key=... --speech-region=... [--storage-connection=... --storage-container=...]
                                                Non-interactive: for the installer to pre-seed the developer's
                                                own accounts on an end user's machine. Never used interactively.
          exportsecretsforinstaller [path]      Reads this machine's already-saved developer secrets and writes
                                                them to an Inno Setup include file (default installer/secrets.local.iss)
                                                for building the installer. Never prints the values themselves.
        """);
}

async Task LoginAsync()
{
    var emailOrVaid = Prompt("VA email or VAID");
    var password = PromptHidden("Password");
    var credentials = new LibraryCredentials { EmailOrVaid = emailOrVaid, Password = password };
    await client.AuthenticateAsync(credentials, CancellationToken.None);
    Console.WriteLine("Logged in and saved credentials.");
}

async Task SearchAsync(string keyword, string? format)
{
    var results = await client.SearchAsync(new SearchQuery { Keyword = keyword, Format = format }, CancellationToken.None);
    PrintTab("Books", results.Books);
    PrintTab("Periodicals", results.Periodicals);
    PrintTab("Music", results.Music);

    static void PrintTab(string label, SearchTabResult tab)
    {
        Console.WriteLine($"\n{label}: {tab.Total} result(s)");
        foreach (var item in tab.Items)
        {
            Console.WriteLine($"  [{item.BookshareId}] {item.Title} — {item.AuthorNames} ({item.Size}) [{item.Status.Key}]");
            foreach (var fmt in item.Formats.Where(f => !string.IsNullOrEmpty(f.FormatId)))
            {
                Console.WriteLine($"      format: {fmt.FormatId} ({fmt.Name})");
            }
        }
    }
}

async Task ShowBookshelfAsync()
{
    var shelf = await client.GetBookshelfAsync(CancellationToken.None);
    Console.WriteLine($"Books/Music on loan: {shelf.TotalBookAndMusicBraille}/{BookshelfSnapshot.LoanCap} (remaining: {shelf.RemainingLoanSlots})");
    foreach (var item in shelf.Books.Concat(shelf.Music))
    {
        Console.WriteLine($"  [{item.ActiveTitleId}] {item.Title} — {item.AuthorDisplay} ({item.Format.Name}) [{item.Status.Key}] added {item.DateAdded} (bookshareId={item.BookshareId})");
    }
    Console.WriteLine($"\nPeriodicals: {shelf.TotalPeriodical}");
    foreach (var item in shelf.Periodicals)
    {
        Console.WriteLine($"  [{item.ActiveTitleId}] {item.Title} ({item.Format.Name}) [{item.Status.Key}] added {item.DateAdded}");
    }
}

async Task AddToBookshelfAsync(string bookshareId, string format)
{
    await client.AddToBookshelfAsync(bookshareId, format, LibraryItemType.Book, CancellationToken.None);
    Console.WriteLine("Added to bookshelf.");
}

async Task RemoveFromBookshelfAsync(string activeTitleId, string? typeArg)
{
    var type = ParseType(typeArg);
    await client.RemoveFromBookshelfAsync(activeTitleId, type, CancellationToken.None);
    Console.WriteLine("Removed from bookshelf.");
}

async Task ShowRequestListAsync()
{
    var items = await client.GetRequestListAsync(CancellationToken.None);
    Console.WriteLine($"Request list: {items.Count} item(s)");
    foreach (var item in items)
    {
        Console.WriteLine($"  [{item.ActiveTitleId}] {item.Title} — {item.AuthorDisplay} added {item.DateAdded} (bookshareId={item.BookshareId})");
    }
}

async Task AddToRequestListAsync(string bookshareId)
{
    await client.AddToRequestListAsync(bookshareId, CancellationToken.None);
    Console.WriteLine("Added to request list.");
}

async Task ShowSubscriptionsAsync()
{
    var subs = await client.GetSubscriptionsAsync(CancellationToken.None);
    Console.WriteLine($"Subscriptions: {subs.Count}");
    foreach (var sub in subs)
    {
        Console.WriteLine($"  [{sub.ActiveTitleId}] {sub.Title}");
    }
}

async Task SubscribeAsync(string bookshareId)
{
    await client.SubscribeAsync(bookshareId, CancellationToken.None);
    Console.WriteLine("Subscribed.");
}

async Task ShowHistoryAsync()
{
    var history = await client.GetHistoryAsync(CancellationToken.None);
    Console.WriteLine($"History: {history.Count} entries (not yet implemented — see docs/va-endpoints.md)");
    foreach (var entry in history)
    {
        Console.WriteLine($"  {entry.Title} — {entry.Author} ({entry.DateLoaned})");
    }
}

async Task ShowRawAsync(string relativeUrl)
{
    await client.GetBookshelfAsync(CancellationToken.None); // cheap call to force authentication via the session manager
    var body = await rawClient.GetRawAsync(relativeUrl, CancellationToken.None);
    Console.WriteLine(body);
}

async Task AzureSpeechSetupAsync()
{
    var key = PromptHidden("Azure Speech key");
    var region = Prompt("Azure Speech region (e.g. australiaeast)");
    var credentials = new AzureSpeechCredentials { Key = key, Region = region };
    await credentials.SaveAsync(credentialStore, CancellationToken.None);
    Console.WriteLine("Saved Azure Speech credentials.");
}

async Task<AzureSpeechRecognizer> CreateAzureRecognizerAsync()
{
    var credentials = await AzureSpeechCredentials.LoadAsync(credentialStore, CancellationToken.None)
        ?? throw new InvalidOperationException("No Azure Speech credentials saved yet — run 'azurespeechsetup' first.");
    return new AzureSpeechRecognizer(credentials);
}

async Task SayAsync(string text)
{
    using var synthesizer = new SapiSpeechSynthesizer();
    Console.WriteLine($"Speaking: \"{text}\"");
    await synthesizer.SpeakAsync(text);
}

async Task SpeechEchoTestAsync(ISpeechRecognizer recognizer)
{
    using var _ = recognizer;
    using var synthesizer = new SapiSpeechSynthesizer();

    Console.WriteLine("Press Enter to start listening (push-to-talk)...");
    Console.ReadLine();
    Console.WriteLine("Listening — speak now, then press Enter to stop.");
    recognizer.StartListening();
    Console.ReadLine();

    var text = await recognizer.StopListeningAsync();
    Console.WriteLine(string.IsNullOrWhiteSpace(text) ? "Recognized: (nothing understood)" : $"Recognized: \"{text}\"");

    var reply = string.IsNullOrWhiteSpace(text) ? "I didn't catch that." : $"You said: {text}";
    await synthesizer.SpeakAsync(reply);
}

async Task ClaudeSetupAsync()
{
    var key = PromptHidden("Anthropic API key");
    await credentialStore.SetSecretAsync(CredentialKeys.AnthropicApiKey, key, CancellationToken.None);
    Console.WriteLine("Saved Anthropic API key.");
}

async Task MemorySetupAsync()
{
    var connectionString = PromptHidden("Azure Storage connection string");
    var containerName = Prompt("Container name");
    var credentials = new AzureMemoryStoreCredentials { ConnectionString = connectionString, ContainerName = containerName };
    await credentials.SaveAsync(credentialStore, CancellationToken.None);
    Console.WriteLine("Saved Azure memory-store credentials.");
}

async Task ListContainersAsync()
{
    var credentials = await AzureMemoryStoreCredentials.LoadAsync(credentialStore, CancellationToken.None)
        ?? throw new InvalidOperationException("No Azure memory-store credentials saved yet — run 'memorysetup' first (container name doesn't matter for this check).");
    var service = new BlobServiceClient(credentials.ConnectionString);
    Console.WriteLine("Containers in this storage account:");
    await foreach (var container in service.GetBlobContainersAsync())
    {
        Console.WriteLine($"  {container.Name}");
    }
}

async Task ChatAsync(bool dryRun)
{
    var apiKey = await credentialStore.GetSecretAsync(CredentialKeys.AnthropicApiKey, CancellationToken.None);
    IBrain brain = apiKey is not null ? new ClaudeBrain(apiKey) : new StubBrain();
    Console.WriteLine(apiKey is not null
        ? "Using ClaudeBrain (real Anthropic API)."
        : "No Anthropic API key saved — using StubBrain (canned responses). Run 'claudesetup' to use the real thing.");

    var memoryCredentials = await AzureMemoryStoreCredentials.LoadAsync(credentialStore, CancellationToken.None);
    IMemoryStore memoryStore;
    if (memoryCredentials is not null)
    {
        memoryStore = new AzureBlobMemoryStore(memoryCredentials.ConnectionString, memoryCredentials.ContainerName);
    }
    else
    {
        memoryStore = new InMemoryMemoryStore();
        Console.WriteLine("No Azure memory-store credentials saved — using in-session-only memory. Run 'memorysetup' to persist across runs.");
    }

    var toolExecutor = new ToolCallExecutor(client, dryRun);
    var orchestrator = new LibrarianOrchestrator(brain, toolExecutor, client, memoryStore);

    Console.WriteLine("Initializing (loading bookshelf, history, and remembered preferences)...");
    await orchestrator.InitializeAsync(CancellationToken.None);
    Console.WriteLine(dryRun
        ? "Chat ready (DRY RUN — no real bookshelf/request-list/subscription changes will be made). Type 'exit' to quit."
        : "Chat ready. Type 'exit' to quit.");

    while (true)
    {
        Console.Write("\nyou> ");
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input) || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }
        var response = await orchestrator.ProcessUserUtteranceAsync(input, CancellationToken.None);
        Console.WriteLine($"bookworm> {response}");
    }

    await orchestrator.SaveMemoryAsync(CancellationToken.None);
    Console.WriteLine("Memory saved. Bye.");

    (brain as IDisposable)?.Dispose();
}

async Task SeedSecretsAsync(string[] rawArgs)
{
    string? NamedArg(string name)
    {
        var prefix = $"--{name}=";
        var match = rawArgs.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?[prefix.Length..];
    }

    var claudeKey = NamedArg("claude-key") ?? throw new ArgumentException("Missing --claude-key");
    var speechKey = NamedArg("speech-key") ?? throw new ArgumentException("Missing --speech-key");
    var speechRegion = NamedArg("speech-region") ?? throw new ArgumentException("Missing --speech-region");
    var storageConnection = NamedArg("storage-connection");
    var storageContainer = NamedArg("storage-container");

    await credentialStore.SetSecretAsync(CredentialKeys.AnthropicApiKey, claudeKey, CancellationToken.None);
    await new AzureSpeechCredentials { Key = speechKey, Region = speechRegion }.SaveAsync(credentialStore, CancellationToken.None);

    if (storageConnection is not null && storageContainer is not null)
    {
        await new AzureMemoryStoreCredentials { ConnectionString = storageConnection, ContainerName = storageContainer }.SaveAsync(credentialStore, CancellationToken.None);
    }

    Console.WriteLine("Seeded developer-owned credentials (Anthropic, Azure Speech, Azure Storage) into this machine's Credential Manager.");
}

async Task ExportSecretsForInstallerAsync(string outputPath)
{
    var claudeKey = await credentialStore.GetSecretAsync(CredentialKeys.AnthropicApiKey, CancellationToken.None)
        ?? throw new InvalidOperationException("No Anthropic API key saved on this machine — run 'claudesetup' first.");
    var speech = await AzureSpeechCredentials.LoadAsync(credentialStore, CancellationToken.None)
        ?? throw new InvalidOperationException("No Azure Speech credentials saved on this machine — run 'azurespeechsetup' first.");
    var memory = await AzureMemoryStoreCredentials.LoadAsync(credentialStore, CancellationToken.None);

    static string Escape(string s) => s.Replace("\"", "\"\""); // Inno Setup preprocessor string escaping

    var fullPath = Path.GetFullPath(outputPath);
    var dir = Path.GetDirectoryName(fullPath);
    if (dir is not null)
    {
        Directory.CreateDirectory(dir);
    }

    var lines = new List<string>
    {
        "; Auto-generated by 'dotnet run --project src/Bookworm.Console -- exportsecretsforinstaller'.",
        "; Contains real secrets — never commit this file (see .gitignore).",
        $"#define ClaudeApiKey \"{Escape(claudeKey)}\"",
        $"#define SpeechKey \"{Escape(speech.Key)}\"",
        $"#define SpeechRegion \"{Escape(speech.Region)}\"",
        $"#define StorageConnectionString \"{Escape(memory?.ConnectionString ?? "")}\"",
        $"#define StorageContainer \"{Escape(memory?.ContainerName ?? "")}\"",
    };

    await File.WriteAllLinesAsync(fullPath, lines, CancellationToken.None);
    Console.WriteLine($"Wrote secret defines to {fullPath} (values not shown here).");
    if (memory is null)
    {
        Console.WriteLine("Note: no Azure memory-store credentials found — the installer will still work, just without cross-session memory.");
    }
}

static LibraryItemType ParseType(string? typeArg) => typeArg?.ToLowerInvariant() switch
{
    "music" => LibraryItemType.Music,
    "periodical" => LibraryItemType.Periodical,
    _ => LibraryItemType.Book,
};
