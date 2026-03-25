using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaudeSW.Api
{
    public class ProviderDescriptor
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string DefaultModel { get; set; }
        public List<string> Models { get; set; } = new List<string>();
    }

    public class UsageSnapshot
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TotalTokens { get; set; }
    }

    public class NormalizedToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public JObject Arguments { get; set; }
    }

    public class ToolApprovalRequest
    {
        public string ToolName { get; set; }
        public JObject Arguments { get; set; }
        public string Summary { get; set; }
    }

    public class ToolApprovalDecision
    {
        public bool Approved { get; set; }
        public string Reason { get; set; }

        public static ToolApprovalDecision Approve()
        {
            return new ToolApprovalDecision { Approved = true };
        }

        public static ToolApprovalDecision Reject(string reason)
        {
            return new ToolApprovalDecision
            {
                Approved = false,
                Reason = reason
            };
        }
    }

    public class ConversationBlock
    {
        public string Type { get; set; }
        public string Text { get; set; }
        public string ToolCallId { get; set; }
        public string ToolName { get; set; }
        public JObject Arguments { get; set; }
        public string ResultContent { get; set; }
        public bool? IsError { get; set; }

        public static ConversationBlock TextBlock(string text)
        {
            return new ConversationBlock { Type = "text", Text = text };
        }

        public static ConversationBlock ToolCallBlock(string id, string name, JObject arguments)
        {
            return new ConversationBlock
            {
                Type = "tool_call",
                ToolCallId = id,
                ToolName = name,
                Arguments = arguments
            };
        }

        public static ConversationBlock ToolResultBlock(string toolCallId, string resultContent, bool isError)
        {
            return new ConversationBlock
            {
                Type = "tool_result",
                ToolCallId = toolCallId,
                ResultContent = resultContent,
                IsError = isError ? true : (bool?)null
            };
        }
    }

    public class ConversationMessage
    {
        public string Role { get; set; }
        public List<ConversationBlock> Blocks { get; set; } = new List<ConversationBlock>();

        public static ConversationMessage FromText(string role, string text)
        {
            return new ConversationMessage
            {
                Role = role,
                Blocks = new List<ConversationBlock> { ConversationBlock.TextBlock(text) }
            };
        }

        public static ConversationMessage FromToolResults(IEnumerable<ConversationBlock> blocks)
        {
            return new ConversationMessage
            {
                Role = "tool",
                Blocks = new List<ConversationBlock>(blocks)
            };
        }
    }

    public class ProviderTurnRequest
    {
        public string Model { get; set; }
        public string SystemPrompt { get; set; }
        public List<ConversationMessage> Conversation { get; set; } = new List<ConversationMessage>();
        public List<ToolDefinition> Tools { get; set; } = new List<ToolDefinition>();
    }

    public class ProviderTurnResponse
    {
        public ConversationMessage AssistantMessage { get; set; }
        public List<NormalizedToolCall> ToolCalls { get; set; } = new List<NormalizedToolCall>();
        public UsageSnapshot Usage { get; set; } = new UsageSnapshot();
        public string StopReason { get; set; }
        public bool IsIncomplete { get; set; }
        public string IncompleteReason { get; set; }
    }

    public class AgentTurnResult
    {
        public string AssistantText { get; set; }
        public bool ConversationReset { get; set; }
        public string ConversationResetReason { get; set; }
        public UsageSnapshot Usage { get; set; } = new UsageSnapshot();
    }

    public interface IModelProvider : IDisposable
    {
        ProviderDescriptor Descriptor { get; }
        bool IsConfigured { get; }
        void SetApiKey(string apiKey);
        Task<string> ValidateKeyAsync(string model, System.Threading.CancellationToken ct);
        Task<ProviderTurnResponse> GenerateTurnAsync(ProviderTurnRequest request, System.Threading.CancellationToken ct);
    }

    public class ToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("input_schema")]
        public JObject InputSchema { get; set; }

        [JsonIgnore]
        public string Category { get; set; }

        [JsonIgnore]
        public bool IsReadOnly { get; set; }

        [JsonIgnore]
        public bool RequiresConfirmation { get; set; }
    }

    public class AnthropicMessagesRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("max_tokens")]
        public int MaxTokens { get; set; } = 4096;

        [JsonProperty("system")]
        public string System { get; set; }

        [JsonProperty("messages")]
        public List<AnthropicMessage> Messages { get; set; } = new List<AnthropicMessage>();

        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public List<ToolDefinition> Tools { get; set; }
    }

    public class AnthropicMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public object Content { get; set; }
    }

    public class AnthropicContentBlock
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public string Id { get; set; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string Name { get; set; }

        [JsonProperty("input", NullValueHandling = NullValueHandling.Ignore)]
        public JObject Input { get; set; }

        [JsonProperty("tool_use_id", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolUseId { get; set; }

        [JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
        public object ResultContent { get; set; }

        [JsonProperty("is_error", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsError { get; set; }
    }

    public class AnthropicMessagesResponse
    {
        [JsonProperty("content")]
        public List<AnthropicContentBlock> Content { get; set; }

        [JsonProperty("stop_reason")]
        public string StopReason { get; set; }

        [JsonProperty("usage")]
        public AnthropicUsage Usage { get; set; }
    }

    public class AnthropicUsage
    {
        [JsonProperty("input_tokens")]
        public int InputTokens { get; set; }

        [JsonProperty("output_tokens")]
        public int OutputTokens { get; set; }
    }

    public class OpenAIChatCompletionsRequest
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("messages")]
        public List<OpenAIChatMessage> Messages { get; set; } = new List<OpenAIChatMessage>();

        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public List<OpenAITool> Tools { get; set; }

        [JsonProperty("tool_choice", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolChoice { get; set; }

        [JsonProperty("parallel_tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public bool? ParallelToolCalls { get; set; }

        [JsonProperty("max_completion_tokens", NullValueHandling = NullValueHandling.Ignore)]
        public int? MaxCompletionTokens { get; set; }

        [JsonProperty("temperature", NullValueHandling = NullValueHandling.Ignore)]
        public double? Temperature { get; set; }
    }

    public class OpenAIChatMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content", NullValueHandling = NullValueHandling.Ignore)]
        public string Content { get; set; }

        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string ToolCallId { get; set; }

        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<OpenAIToolCall> ToolCalls { get; set; }
    }

    public class OpenAITool
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("function")]
        public OpenAIFunctionDefinition Function { get; set; }
    }

    public class OpenAIFunctionDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("parameters")]
        public JObject Parameters { get; set; }

        [JsonProperty("strict", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Strict { get; set; }
    }

    public class OpenAIToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("function")]
        public OpenAIFunctionCall Function { get; set; }
    }

    public class OpenAIFunctionCall
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public string Arguments { get; set; }
    }

    public class OpenAIChatCompletionsResponse
    {
        [JsonProperty("choices")]
        public List<OpenAIChoice> Choices { get; set; }

        [JsonProperty("usage")]
        public OpenAIUsage Usage { get; set; }
    }

    public class OpenAIChoice
    {
        [JsonProperty("finish_reason")]
        public string FinishReason { get; set; }

        [JsonProperty("message")]
        public OpenAIChatMessage Message { get; set; }
    }

    public class OpenAIUsage
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
