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
    public class OpenAIModelProvider : IModelProvider
    {
        private const string BaseUrl = "https://api.openai.com/v1/chat/completions";

        private readonly HttpClient _http;
        private string _apiKey;

        public OpenAIModelProvider()
        {
            Descriptor = new ProviderDescriptor
            {
                Key = "openai",
                DisplayName = "OpenAI",
                DefaultModel = "gpt-5.4",
                Models = new List<string>
                {
                    "gpt-5.4",
                    "gpt-5.4-mini",
                    "gpt-4.1"
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
                var request = new OpenAIChatCompletionsRequest
                {
                    Model = model,
                    Messages = new List<OpenAIChatMessage>
                    {
                        new OpenAIChatMessage { Role = "user", Content = "ping" }
                    },
                    MaxCompletionTokens = 16,
                    Temperature = 0
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
            var chatRequest = new OpenAIChatCompletionsRequest
            {
                Model = request.Model,
                Messages = BuildMessages(request.SystemPrompt, request.Conversation),
                Tools = request.Tools != null && request.Tools.Count > 0 ? BuildTools(request.Tools) : null,
                ToolChoice = request.Tools != null && request.Tools.Count > 0 ? "auto" : null,
                ParallelToolCalls = false,
                MaxCompletionTokens = 2048,
                Temperature = 0.2
            };

            var response = await SendRequestAsync(chatRequest, ct);
            var choice = response.Choices != null && response.Choices.Count > 0 ? response.Choices[0] : null;
            var assistantMessage = new ConversationMessage { Role = "assistant" };
            var toolCalls = new List<NormalizedToolCall>();

            if (choice != null && choice.Message != null)
            {
                if (!string.IsNullOrWhiteSpace(choice.Message.Content))
                    assistantMessage.Blocks.Add(ConversationBlock.TextBlock(choice.Message.Content));

                foreach (var toolCall in choice.Message.ToolCalls ?? new List<OpenAIToolCall>())
                {
                    JObject args;
                    try
                    {
                        args = string.IsNullOrWhiteSpace(toolCall.Function?.Arguments)
                            ? new JObject()
                            : JObject.Parse(toolCall.Function.Arguments);
                    }
                    catch
                    {
                        args = new JObject { ["raw_arguments"] = toolCall.Function?.Arguments ?? "" };
                    }

                    assistantMessage.Blocks.Add(ConversationBlock.ToolCallBlock(toolCall.Id, toolCall.Function?.Name, args));
                    toolCalls.Add(new NormalizedToolCall
                    {
                        Id = toolCall.Id,
                        Name = toolCall.Function?.Name,
                        Arguments = args
                    });
                }
            }

            return new ProviderTurnResponse
            {
                AssistantMessage = assistantMessage,
                ToolCalls = toolCalls,
                StopReason = choice?.FinishReason,
                IsIncomplete = string.Equals(choice?.FinishReason, "length", StringComparison.OrdinalIgnoreCase),
                IncompleteReason = choice?.FinishReason,
                Usage = new UsageSnapshot
                {
                    InputTokens = response.Usage?.PromptTokens ?? 0,
                    OutputTokens = response.Usage?.CompletionTokens ?? 0,
                    TotalTokens = response.Usage?.TotalTokens ?? 0
                }
            };
        }

        private List<OpenAIChatMessage> BuildMessages(string systemPrompt, List<ConversationMessage> conversation)
        {
            var messages = new List<OpenAIChatMessage>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new OpenAIChatMessage
                {
                    Role = "system",
                    Content = systemPrompt
                });
            }

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
                        messages.Add(new OpenAIChatMessage
                        {
                            Role = "user",
                            Content = text
                        });
                    }
                }
                else if (message.Role == "assistant")
                {
                    var assistantText = string.Join("\n", message.Blocks
                        .Where(b => b.Type == "text" && !string.IsNullOrWhiteSpace(b.Text))
                        .Select(b => b.Text));

                    var toolCalls = message.Blocks
                        .Where(b => b.Type == "tool_call")
                        .Select(b => new OpenAIToolCall
                        {
                            Id = b.ToolCallId,
                            Type = "function",
                            Function = new OpenAIFunctionCall
                            {
                                Name = b.ToolName,
                                Arguments = (b.Arguments ?? new JObject()).ToString(Formatting.None)
                            }
                        })
                        .ToList();

                    messages.Add(new OpenAIChatMessage
                    {
                        Role = "assistant",
                        Content = string.IsNullOrWhiteSpace(assistantText) ? null : assistantText,
                        ToolCalls = toolCalls.Count > 0 ? toolCalls : null
                    });
                }
                else if (message.Role == "tool")
                {
                    foreach (var block in message.Blocks.Where(b => b.Type == "tool_result"))
                    {
                        messages.Add(new OpenAIChatMessage
                        {
                            Role = "tool",
                            ToolCallId = block.ToolCallId,
                            Content = block.ResultContent ?? ""
                        });
                    }
                }
            }

            return messages;
        }

        private List<OpenAITool> BuildTools(List<ToolDefinition> tools)
        {
            return tools.Select(t => new OpenAITool
            {
                Type = "function",
                Function = new OpenAIFunctionDefinition
                {
                    Name = t.Name,
                    Description = t.Description,
                    Parameters = t.InputSchema,
                    Strict = true
                }
            }).ToList();
        }

        private async Task<OpenAIChatCompletionsResponse> SendRequestAsync(OpenAIChatCompletionsRequest request, CancellationToken ct)
        {
            var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            var httpReq = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            httpReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var httpResp = await _http.SendAsync(httpReq, ct);
            var body = await httpResp.Content.ReadAsStringAsync();
            if (!httpResp.IsSuccessStatusCode)
                throw new HttpRequestException("OpenAI API " + (int)httpResp.StatusCode + ": " + body);

            return JsonConvert.DeserializeObject<OpenAIChatCompletionsResponse>(body);
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
