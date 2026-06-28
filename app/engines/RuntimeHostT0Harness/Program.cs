using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using DemoBoards.RuntimeHost;

namespace RuntimeHostT0Harness;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private const string PortfolioCardId = "card-portfolio-t0-winui";
    private const string DefaultBoardId = "winui-board";
    private const int DefaultAgentfacePort = 43123;

    private static async Task<int> Main(string[] args)
    {
        try
        {
            string boardId = ReadOptionValue(args, "--board-id") ?? DefaultBoardId;
            int port = ReadPositiveIntOption(args, "--port") ?? DefaultAgentfacePort;
            bool requireFixedPort = HasFlag(args, "--require-fixed-port");
            bool serveMode = HasFlag(args, "--serve");

            await using var runtimeService = new DemoBoardsRuntimeService(
                options: new RuntimeHostOptions(port, requireFixedPort, boardId));
            await runtimeService.StartAsync().ConfigureAwait(false);

            using var client = new HttpClient
            {
                BaseAddress = new Uri(runtimeService.GetStatus().AgentfaceEndpoint + "/")
            };

            Console.WriteLine("[T0] Runtime started at " + client.BaseAddress);

            if (serveMode)
            {
                Console.WriteLine($"[serve] READY endpoint={client.BaseAddress} boardId={boardId}");
                await WaitForExitSignalAsync().ConfigureAwait(false);
                Console.WriteLine("[serve] STOPPED");
                return 0;
            }

            JsonObject portfolioSeedCard = LoadPortfolioFixture();
            portfolioSeedCard["id"] = PortfolioCardId;

            JsonNode? upsertData = await CallMcpSuccessAsync(
                client,
                "mcp-controlplane",
                "manage.upsert-card",
                new JsonObject
                {
                    ["board_id"] = boardId,
                    ["card_id"] = PortfolioCardId,
                    ["candidate_card_content"] = portfolioSeedCard.DeepClone(),
                },
                "T0 manage.upsert-card").ConfigureAwait(false);
            Console.WriteLine("[T0] upsert payload: " + JsonSerializer.Serialize(upsertData, JsonOptions));

            if (upsertData is null)
            {
                throw new InvalidOperationException("T0 manage.upsert-card returned no data payload.");
            }

            JsonNode? readData = await CallMcpSuccessAsync(
                client,
                "mcp-controlplane",
                "manage.read-card",
                new JsonObject
                {
                    ["board_id"] = boardId,
                    ["card_id"] = PortfolioCardId,
                },
                "T0 manage.read-card").ConfigureAwait(false);
            Console.WriteLine("[T0] read payload: " + JsonSerializer.Serialize(readData, JsonOptions));

            JsonObject storedCard = ReadStoredCard(readData) ?? throw new InvalidOperationException("T0 manage.read-card returned no card.");

            if (!JsonNode.DeepEquals(storedCard, portfolioSeedCard))
            {
                throw new InvalidOperationException("T0 stored card mismatch for seeded portfolio card.");
            }

            var pollResult = await PollBoardStatusAsync(client, boardId, attempts: 8, gapMs: 1000).ConfigureAwait(false);
            if (!pollResult.Matched)
            {
                throw new InvalidOperationException("T0 timed out waiting for card to reach completed status.");
            }

            Console.WriteLine("[T0] stored card matches seeded card");
            Console.WriteLine($"[T0] completed in {pollResult.AttemptsUsed} poll(s)");
            Console.WriteLine("[harness] T0 PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[harness] T0 FAIL");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static async Task WaitForExitSignalAsync()
    {
        while (true)
        {
            string? line = await Console.In.ReadLineAsync().ConfigureAwait(false);
            if (line is null)
            {
                return;
            }

            string trimmed = line.Trim();
            if (string.Equals(trimmed, "exit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "quit", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "stop", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    private static string? ReadOptionValue(string[] args, string optionName)
    {
        for (int i = 0; i < args.Length - 1; i += 1)
        {
            if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
            {
                string value = args[i + 1]?.Trim() ?? string.Empty;
                return value.Length > 0 ? value : null;
            }
        }

        return null;
    }

    private static int? ReadPositiveIntOption(string[] args, string optionName)
    {
        string? raw = ReadOptionValue(args, optionName);
        return int.TryParse(raw, out int value) && value > 0 ? value : null;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (string arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static JsonObject LoadPortfolioFixture()
    {
        string fixturePath = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "cardT-portfolio.json");
        if (!System.IO.File.Exists(fixturePath))
        {
            throw new System.IO.FileNotFoundException("Missing fixture cardT-portfolio.json", fixturePath);
        }

        JsonNode? parsed = JsonNode.Parse(System.IO.File.ReadAllText(fixturePath));
        if (parsed is not JsonObject jsonObject)
        {
            throw new InvalidOperationException("Fixture cardT-portfolio.json must be a JSON object.");
        }

        return jsonObject;
    }

    private static async Task<JsonNode?> CallMcpSuccessAsync(HttpClient client, string route, string tool, JsonObject args, string label)
    {
        string requestBody = JsonSerializer.Serialize(new JsonObject
        {
            ["tool"] = tool,
            ["args"] = args,
        }, JsonOptions);

        using var response = await client.PostAsync(
            route,
            new StringContent(requestBody, Encoding.UTF8, "application/json")).ConfigureAwait(false);

        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{label} returned HTTP {(int)response.StatusCode}: {responseBody}");
        }

        JsonObject root = ParseRequiredObject(responseBody, label + " response");

        string status = root["status"]?.GetValue<string>() ?? string.Empty;
        if (string.Equals(status, "fail", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{label} failed: {responseBody}");
        }

        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
            && root["data"] is not null)
        {
            return root["data"];
        }

        // Some embedded MCP tools return the payload directly (no status/data envelope).
        return root;
    }

    private static JsonObject? ReadStoredCard(JsonNode? readData)
    {
        if (readData is JsonArray cards && cards.Count > 0)
        {
            return cards[0] as JsonObject;
        }

        JsonArray? wrappedCards = (readData as JsonObject)?["cards"] as JsonArray;
        if (wrappedCards is null || wrappedCards.Count == 0)
        {
            return null;
        }

        return wrappedCards[0] as JsonObject;
    }

    private static async Task<(bool Matched, int AttemptsUsed)> PollBoardStatusAsync(HttpClient client, string boardId, int attempts, int gapMs)
    {
        for (int attempt = 1; attempt <= attempts; attempt += 1)
        {
            JsonNode? statusData = await CallMcpSuccessAsync(
                client,
                "mcp-controlplane",
                "list-runtime-cards",
                new JsonObject { ["board_id"] = boardId },
                "list-runtime-cards").ConfigureAwait(false);

            JsonObject? card = FindBoardStatusCard(statusData, PortfolioCardId);
            string status = card?["status"]?.GetValue<string>() ?? string.Empty;
            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                return (true, attempt);
            }

            if (attempt < attempts)
            {
                await Task.Delay(gapMs).ConfigureAwait(false);
            }
        }

        return (false, attempts);
    }

    private static JsonObject? FindBoardStatusCard(JsonNode? statusData, string cardId)
    {
        JsonArray? cards = statusData as JsonArray;
        if (cards is null)
        {
            cards = (statusData as JsonObject)?["cards"] as JsonArray;
        }
        if (cards is null)
        {
            return null;
        }

        foreach (JsonNode? cardNode in cards)
        {
            if (cardNode is not JsonObject card)
            {
                continue;
            }

            string candidate = card["card-id"]?.GetValue<string>()
                ?? card["name"]?.GetValue<string>()
                ?? card["id"]?.GetValue<string>()
                ?? string.Empty;
            if (string.Equals(candidate, cardId, StringComparison.Ordinal))
            {
                return card;
            }
        }

        return null;
    }

    private static JsonObject ParseRequiredObject(string rawJson, string label)
    {
        JsonNode? parsed = JsonNode.Parse(string.IsNullOrWhiteSpace(rawJson) ? "{}" : rawJson);
        if (parsed is not JsonObject jsonObject)
        {
            throw new InvalidOperationException($"{label} must be a JSON object.");
        }

        return jsonObject;
    }
}
