using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ClaudeSW.Api
{
    public class AgentSessionOrchestrator
    {
        private const int MaxToolLoopIterations = 15;

        private readonly ModelProviderRegistry _providers;
        private readonly List<ConversationMessage> _history = new List<ConversationMessage>();
        private string _lastDocumentIdentity;

        public AgentSessionOrchestrator(ModelProviderRegistry providers)
        {
            _providers = providers;
        }

        public event Action<string, string> OnToolCall;
        public event Action<int, int> OnUsageUpdate;

        public ModelProviderRegistry Providers
        {
            get { return _providers; }
        }

        public void ResetConversation(string reason = null)
        {
            _history.Clear();
            _lastDocumentIdentity = null;
        }

        public async Task<AgentTurnResult> RunTurnAsync(
            string systemPrompt,
            string userMessage,
            List<ToolDefinition> tools,
            Func<string, JObject, Task<(string result, bool isError)>> executeToolAsync,
            Func<ToolApprovalRequest, Task<ToolApprovalDecision>> approvalAsync,
            Func<Task<string>> getDocumentIdentityAsync,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                throw new ArgumentException("User message cannot be empty.");

            var turnResult = new AgentTurnResult();
            var currentDocumentIdentity = getDocumentIdentityAsync != null
                ? await getDocumentIdentityAsync()
                : "(unknown-document)";

            if (_history.Count > 0 &&
                !string.Equals(_lastDocumentIdentity ?? "", currentDocumentIdentity ?? "", StringComparison.Ordinal))
            {
                _history.Clear();
                turnResult.ConversationReset = true;
                turnResult.ConversationResetReason = "Active SolidWorks document changed. Started a new AI session.";
            }

            _lastDocumentIdentity = currentDocumentIdentity;
            _history.Add(ConversationMessage.FromText("user", userMessage));

            var textParts = new List<string>();
            int totalInput = 0;
            int totalOutput = 0;
            int totalTokens = 0;

            for (int iteration = 0; iteration < MaxToolLoopIterations; iteration++)
            {
                ct.ThrowIfCancellationRequested();

                var providerResponse = await _providers.GenerateTurnAsync(new ProviderTurnRequest
                {
                    SystemPrompt = systemPrompt,
                    Conversation = new List<ConversationMessage>(_history),
                    Tools = tools
                }, ct);

                totalInput += providerResponse.Usage.InputTokens;
                totalOutput += providerResponse.Usage.OutputTokens;
                totalTokens += providerResponse.Usage.TotalTokens;
                OnUsageUpdate?.Invoke(totalInput, totalOutput);

                if (providerResponse.AssistantMessage != null && providerResponse.AssistantMessage.Blocks.Count > 0)
                {
                    _history.Add(providerResponse.AssistantMessage);
                    textParts.AddRange(providerResponse.AssistantMessage.Blocks
                        .Where(b => b.Type == "text" && !string.IsNullOrWhiteSpace(b.Text))
                        .Select(b => b.Text));
                }

                if (providerResponse.ToolCalls == null || providerResponse.ToolCalls.Count == 0)
                {
                    if (providerResponse.IsIncomplete)
                        throw new InvalidOperationException("Model response was incomplete (" + providerResponse.IncompleteReason + ").");

                    turnResult.AssistantText = string.Join("\n", textParts).Trim();
                    turnResult.Usage = new UsageSnapshot
                    {
                        InputTokens = totalInput,
                        OutputTokens = totalOutput,
                        TotalTokens = totalTokens
                    };
                    return turnResult;
                }

                var toolResults = new List<ConversationBlock>();
                foreach (var toolCall in providerResponse.ToolCalls)
                {
                    ct.ThrowIfCancellationRequested();
                    var tool = tools.FirstOrDefault(t => t.Name == toolCall.Name);
                    if (tool == null)
                    {
                        toolResults.Add(ConversationBlock.ToolResultBlock(
                            toolCall.Id,
                            JsonConvert.SerializeObject(new
                            {
                                status = "error",
                                message = "Unknown tool requested by model.",
                                tool = toolCall.Name
                            }),
                            true));
                        continue;
                    }

                    OnToolCall?.Invoke(toolCall.Name, (toolCall.Arguments ?? new JObject()).ToString(Formatting.None));

                    if (tool.RequiresConfirmation)
                    {
                        var approvalRequest = new ToolApprovalRequest
                        {
                            ToolName = toolCall.Name,
                            Arguments = toolCall.Arguments,
                            Summary = tool.Name + " [" + tool.Category + "] " +
                                      ((toolCall.Arguments ?? new JObject()).ToString(Formatting.None))
                        };

                        var approval = approvalAsync != null
                            ? await approvalAsync(approvalRequest)
                            : ToolApprovalDecision.Reject("No approval callback registered.");

                        if (!approval.Approved)
                        {
                            toolResults.Add(ConversationBlock.ToolResultBlock(
                                toolCall.Id,
                                JsonConvert.SerializeObject(new
                                {
                                    status = "error",
                                    message = "User rejected tool execution.",
                                    tool = toolCall.Name,
                                    reason = approval.Reason
                                }),
                                true));
                            continue;
                        }
                    }

                    var execution = await executeToolAsync(toolCall.Name, toolCall.Arguments ?? new JObject());
                    toolResults.Add(ConversationBlock.ToolResultBlock(toolCall.Id, execution.result, execution.isError));
                }

                _history.Add(ConversationMessage.FromToolResults(toolResults));
            }

            throw new InvalidOperationException("Agent loop reached the maximum of 15 tool iterations.");
        }
    }
}
