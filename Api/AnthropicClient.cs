using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaudeSW.Api
{
    public class AnthropicModelProvider : IModelProvider
    {
        private const string BaseUrl = "https://api.anthropic.com/v1/messages";
        private const string ApiVersion = "2023-06-01";

        private readonly HttpClient _http;
        private string _apiKey;

        public AnthropicModelProvider()
        {
            Descriptor = new ProviderDescriptor
            {
                Key = "anthropic",
                DisplayName = "Anthropic",
                DefaultModel = "claude-sonnet-4-20250514",
                Models = new List<string>
                {
                    "claude-sonnet-4-6",
                    "claude-opus-4-6",
                    "claude-haiku-4-5-20251001"
                }
            };

            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromMinutes(3);
        }

        public ProviderDescriptor Descriptor { get; private set; }

        public bool IsConfigured
        {
            get { return !string.IsNullOrWhiteSpace(_apiKey); }
        }

        public void SetApiKey(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<string> ValidateKeyAsync(string model, CancellationToken ct)
        {
            try
            {
                var request = new AnthropicMessagesRequest
                {
                    Model = model,
                    MaxTokens = 16,
                    Messages = new List<AnthropicMessage>
                    {
                        new AnthropicMessage { Role = "user", Content = "ping" }
                    }
                };

                var response = await SendRequestAsync(request, ct);
                return response == null ? "Empty response" : null;
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message.Contains("401")) return "Invalid API key (401 Unauthorized)";
                if (ex.Message.Contains("403")) return "API key lacks required permissions (403)";
                return "Network error: " + ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        public async Task<ProviderTurnResponse> GenerateTurnAsync(ProviderTurnRequest request, CancellationToken ct)
        {
            var anthropicRequest = new AnthropicMessagesRequest
            {
                Model = request.Model,
                MaxTokens = 4096,
                System = request.SystemPrompt,
                Messages = BuildMessages(request.Conversation),
                Tools = request.Tools != null && request.Tools.Count > 0 ? request.Tools : null
            };

            var response = await SendRequestAsync(anthropicRequest, ct);
            var assistantMessage = new ConversationMessage { Role = "assistant" };
            var toolCalls = new List<NormalizedToolCall>();

            foreach (var block in response.Content ?? new List<AnthropicContentBlock>())
            {
                if (block.Type == "text" && !string.IsNullOrWhiteSpace(block.Text))
                {
                    assistantMessage.Blocks.Add(ConversationBlock.TextBlock(block.Text));
                }
                else if (block.Type == "tool_use")
                {
                    assistantMessage.Blocks.Add(ConversationBlock.ToolCallBlock(block.Id, block.Name, block.Input));
                    toolCalls.Add(new NormalizedToolCall
                    {
                        Id = block.Id,
                        Name = block.Name,
                        Arguments = block.Input
                    });
                }
            }

            return new ProviderTurnResponse
            {
                AssistantMessage = assistantMessage,
                ToolCalls = toolCalls,
                StopReason = response.StopReason,
                IsIncomplete = string.Equals(response.StopReason, "max_tokens", StringComparison.OrdinalIgnoreCase),
                IncompleteReason = response.StopReason,
                Usage = new UsageSnapshot
                {
                    InputTokens = response.Usage?.InputTokens ?? 0,
                    OutputTokens = response.Usage?.OutputTokens ?? 0,
                    TotalTokens = (response.Usage?.InputTokens ?? 0) + (response.Usage?.OutputTokens ?? 0)
                }
            };
        }

        private List<AnthropicMessage> BuildMessages(List<ConversationMessage> conversation)
        {
            var messages = new List<AnthropicMessage>();

            foreach (var message in conversation)
            {
                if (message == null || message.Blocks == null || message.Blocks.Count == 0)
                    continue;

                if (message.Role == "user")
                {
                    var text = string.Join("\n", message.Blocks
                        .Where(b => b.Type == "text" && !string.IsNullOrWhiteSpace(b.Text))
                        .Select(b => b.Text));

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        messages.Add(new AnthropicMessage
                        {
                            Role = "user",
                            Content = text
                        });
                    }
                }
                else if (message.Role == "assistant")
                {
                    var content = new List<AnthropicContentBlock>();
                    foreach (var block in message.Blocks)
                    {
                        if (block.Type == "text" && !string.IsNullOrWhiteSpace(block.Text))
                        {
                            content.Add(new AnthropicContentBlock { Type = "text", Text = block.Text });
                        }
                        else if (block.Type == "tool_call")
                        {
                            content.Add(new AnthropicContentBlock
                            {
                                Type = "tool_use",
                                Id = block.ToolCallId,
                                Name = block.ToolName,
                                Input = block.Arguments ?? new JObject()
                            });
                        }
                    }

                    if (content.Count > 0)
                    {
                        messages.Add(new AnthropicMessage
                        {
                            Role = "assistant",
                            Content = content
                        });
                    }
                }
                else if (message.Role == "tool")
                {
                    var content = message.Blocks
                        .Where(b => b.Type == "tool_result")
                        .Select(b => new AnthropicContentBlock
                        {
                            Type = "tool_result",
                            ToolUseId = b.ToolCallId,
                            ResultContent = b.ResultContent,
                            IsError = b.IsError
                        })
                        .ToList();

                    if (content.Count > 0)
                    {
                        messages.Add(new AnthropicMessage
                        {
                            Role = "user",
                            Content = content
                        });
                    }
                }
            }

            return messages;
        }

        private async Task<AnthropicMessagesResponse> SendRequestAsync(AnthropicMessagesRequest request, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            var httpReq = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            httpReq.Headers.Add("x-api-key", _apiKey);
            httpReq.Headers.Add("anthropic-version", ApiVersion);
            httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var httpResp = await _http.SendAsync(httpReq, ct);
            var body = await httpResp.Content.ReadAsStringAsync();
            if (!httpResp.IsSuccessStatusCode)
                throw new HttpRequestException("Anthropic API " + (int)httpResp.StatusCode + ": " + body);

            return JsonConvert.DeserializeObject<AnthropicMessagesResponse>(body);
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
