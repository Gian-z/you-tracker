using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using YouTracker.Core.Abstractions;
using YouTracker.Core.Config;

namespace YouTracker.Infrastructure.Anthropic;

/// <summary>Claude-backed <see cref="IAiProvider"/> using the official Anthropic C# SDK.</summary>
public sealed class AnthropicAiProvider : IAiProvider
{
    private const int MaxTokens = 8192;

    private readonly AnthropicClient _client;
    private readonly string _model;

    public AnthropicAiProvider(AppConfig config)
    {
        _client = new AnthropicClient { ApiKey = config.Anthropic.ApiKey };
        _model = config.Anthropic.Model;
    }

    public Task<string> CompleteTextAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default
    ) => CompleteAsync(systemPrompt, userPrompt, outputConfig: null, ct);

    public Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string jsonSchema,
        CancellationToken ct = default
    )
    {
        Dictionary<string, JsonElement> schema;
        try
        {
            schema =
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonSchema)
                ?? throw new AiProviderException("JSON schema must be a JSON object.");
        }
        catch (JsonException ex)
        {
            throw new AiProviderException($"Invalid JSON schema: {ex.Message}", ex);
        }

        var outputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } };

        // With output_config.format the first content block is text containing valid JSON.
        return CompleteAsync(systemPrompt, userPrompt, outputConfig, ct);
    }

    private async Task<string> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        OutputConfig? outputConfig,
        CancellationToken ct
    )
    {
        Message response;
        try
        {
            response = await _client.Messages.Create(
                new MessageCreateParams
                {
                    Model = _model,
                    MaxTokens = MaxTokens,
                    System = new List<TextBlockParam> { new() { Text = systemPrompt } },
                    Messages = [new() { Role = Role.User, Content = userPrompt }],
                    OutputConfig = outputConfig,
                },
                cancellationToken: ct
            );
        }
        catch (AnthropicApiException ex)
        {
            throw new AiProviderException(
                $"Anthropic API request failed ({ex.GetType().Name}): {ex.Message}",
                ex
            );
        }

        var text = string.Concat(
            response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text)
        );

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new AiProviderException("AI returned empty response");
        }

        return text;
    }
}
