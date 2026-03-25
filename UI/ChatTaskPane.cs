using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ClaudeSW.Api;
using ClaudeSW.Core;
using ClaudeSW.Security;
using ClaudeSW.Tools;

namespace ClaudeSW.UI
{
    public class ChatTaskPane : IDisposable
    {
        private readonly dynamic _swApp;
        private readonly AgentSessionOrchestrator _orchestrator;
        private readonly ModelProviderRegistry _providers;
        private readonly SwToolExecutor _executor;
        private readonly List<ToolDefinition> _tools;

        private dynamic _taskPaneView;
        private ChatControl _chatControl;
        private CancellationTokenSource _cts;

        public ChatTaskPane(object swApp, AgentSessionOrchestrator orchestrator, SwToolExecutor executor)
        {
            _swApp = swApp;
            _orchestrator = orchestrator;
            _providers = orchestrator.Providers;
            _executor = executor;
            _tools = SwToolDefinitions.GetAllTools();

            _orchestrator.OnToolCall += OnToolCall;
            _orchestrator.OnUsageUpdate += OnUsageUpdate;
        }

        public void CreatePane()
        {
            _chatControl = new ChatControl();
            _chatControl.OnSendMessage += HandleSendMessage;
            _chatControl.OnSaveApiKey += HandleSaveApiKey;
            _chatControl.OnClearChat += HandleClearChat;
            _chatControl.OnModelChanged += HandleModelChanged;
            _chatControl.OnProviderChanged += HandleProviderChanged;

            _chatControl.ConfigureProviders(
                _providers.GetProviderDescriptors().ToList(),
                _providers.CurrentProviderKey,
                _providers.CurrentModel);

            UpdateConnectionStatus();

            try
            {
                _taskPaneView = _swApp.CreateTaskpaneView2("", "AI Assistant");
                if (_taskPaneView != null)
                    _taskPaneView.DisplayWindowFromHandlex64(_chatControl.Handle.ToInt64());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TaskPane creation failed: " + ex.Message);
                var form = new Form
                {
                    Text = "SolidWorks AI Assistant",
                    Size = new Size(420, 760),
                    StartPosition = FormStartPosition.CenterScreen,
                    TopMost = true
                };
                _chatControl.Dock = DockStyle.Fill;
                form.Controls.Add(_chatControl);
                form.Show();
            }
        }

        private async void HandleSendMessage(string userMessage)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                return;

            if (!_providers.IsConfigured)
            {
                _chatControl.AppendSystemMessage("API key not configured for " + _providers.CurrentProviderDescriptor.DisplayName + ".");
                return;
            }

            _chatControl.SetBusy(true);
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            _chatControl.AppendUserMessage(userMessage);

            try
            {
                var response = await _orchestrator.RunTurnAsync(
                    SystemPrompts.SolidWorksAgent,
                    userMessage,
                    _tools,
                    _executor.ExecuteAsync,
                    RequestApprovalAsync,
                    _executor.GetActiveDocumentIdentityAsync,
                    _cts.Token);

                if (response.ConversationReset)
                    _chatControl.AppendSystemMessage(response.ConversationResetReason);

                _chatControl.AppendAssistantMessage(
                    string.IsNullOrWhiteSpace(response.AssistantText)
                        ? "No assistant text returned."
                        : response.AssistantText);
            }
            catch (OperationCanceledException)
            {
                _chatControl.AppendSystemMessage("Request cancelled.");
            }
            catch (Exception ex)
            {
                _chatControl.AppendSystemMessage("Error: " + ex.Message);
            }
            finally
            {
                _chatControl.SetBusy(false);
            }
        }

        private Task<ToolApprovalDecision> RequestApprovalAsync(ToolApprovalRequest request)
        {
            _chatControl.AppendApprovalMessage(request.ToolName, request.Arguments?.ToString() ?? "{}");
            bool approved = _chatControl.PromptForApproval(
                request.ToolName,
                request.Arguments != null ? request.Arguments.ToString() : "{}");

            return Task.FromResult(
                approved
                    ? ToolApprovalDecision.Approve()
                    : ToolApprovalDecision.Reject("Rejected by user."));
        }

        private void OnToolCall(string toolName, string argsJson)
        {
            _chatControl.AppendToolMessage(toolName, argsJson);
        }

        private void OnUsageUpdate(int inputTokens, int outputTokens)
        {
            _chatControl.UpdateTokenUsage(inputTokens, outputTokens);
        }

        private async void HandleSaveApiKey(string apiKey)
        {
            _chatControl.SetBusy(true);

            try
            {
                _providers.SetApiKey(_providers.CurrentProviderKey, apiKey);
                var error = await _providers.ValidateCurrentProviderAsync(CancellationToken.None);

                if (error != null)
                {
                    _chatControl.AppendSystemMessage("API key validation failed: " + error);
                }
                else
                {
                    CredentialStore.SaveApiKey(_providers.CurrentProviderKey, apiKey);
                    _chatControl.AppendSystemMessage(
                        _providers.CurrentProviderDescriptor.DisplayName + " API key validated and saved.");
                }
            }
            catch (Exception ex)
            {
                _chatControl.AppendSystemMessage("Failed to save API key: " + ex.Message);
            }
            finally
            {
                UpdateConnectionStatus();
                _chatControl.SetBusy(false);
            }
        }

        private void HandleClearChat()
        {
            _orchestrator.ResetConversation("User cleared chat.");
            _chatControl.ClearChatHistory();
        }

        private void HandleModelChanged(string model)
        {
            _providers.SetCurrentModel(model);
            _chatControl.AppendSystemMessage("Model changed to: " + model);
            UpdateConnectionStatus();
        }

        private void HandleProviderChanged(string providerKey)
        {
            _providers.SetCurrentProvider(providerKey);
            _chatControl.UpdateModelOptions(_providers.CurrentProviderDescriptor, _providers.CurrentModel);
            _chatControl.AppendSystemMessage("Provider changed to: " + _providers.CurrentProviderDescriptor.DisplayName);
            UpdateConnectionStatus();
        }

        private void UpdateConnectionStatus()
        {
            _chatControl.SetConnectionStatus(
                _providers.CurrentProviderDescriptor.DisplayName,
                _providers.IsConfigured);
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _orchestrator.OnToolCall -= OnToolCall;
            _orchestrator.OnUsageUpdate -= OnUsageUpdate;
            _chatControl?.Dispose();

            if (_taskPaneView != null)
            {
                try { _taskPaneView.DeleteView(); } catch { }
                _taskPaneView = null;
            }
        }
    }

    public class ChatControl : UserControl
    {
        private class ComboItem
        {
            public string Value { get; set; }
            public string Label { get; set; }
            public override string ToString() { return Label; }
        }

        public event Action<string> OnSendMessage;
        public event Action<string> OnSaveApiKey;
        public event Action OnClearChat;
        public event Action<string> OnModelChanged;
        public event Action<string> OnProviderChanged;

        private bool _suppressSelectionEvents;
        private Panel _settingsPanel;
        private TextBox _apiKeyBox;
        private Button _saveKeyBtn;
        private ComboBox _providerCombo;
        private ComboBox _modelCombo;
        private Label _statusLabel;
        private RichTextBox _chatBox;
        private TextBox _inputBox;
        private Button _sendBtn;
        private Button _clearBtn;
        private Button _settingsToggle;
        private Label _tokenLabel;

        public ChatControl()
        {
            InitializeLayout();
        }

        public void ConfigureProviders(List<ProviderDescriptor> providers, string selectedProviderKey, string selectedModel)
        {
            _suppressSelectionEvents = true;
            _providerCombo.Items.Clear();

            foreach (var provider in providers)
            {
                _providerCombo.Items.Add(new ComboItem
                {
                    Value = provider.Key,
                    Label = provider.DisplayName
                });
            }

            for (int i = 0; i < _providerCombo.Items.Count; i++)
            {
                var item = _providerCombo.Items[i] as ComboItem;
                if (item != null && item.Value == selectedProviderKey)
                {
                    _providerCombo.SelectedIndex = i;
                    break;
                }
            }

            var descriptor = providers.FirstOrDefault(p => p.Key == selectedProviderKey) ?? providers.First();
            UpdateModelOptions(descriptor, selectedModel);
            _suppressSelectionEvents = false;
        }

        public void UpdateModelOptions(ProviderDescriptor descriptor, string selectedModel)
        {
            _suppressSelectionEvents = true;
            _modelCombo.Items.Clear();

            foreach (var model in descriptor.Models)
                _modelCombo.Items.Add(model);

            var target = string.IsNullOrWhiteSpace(selectedModel) ? descriptor.DefaultModel : selectedModel;
            int index = descriptor.Models.IndexOf(target);
            _modelCombo.SelectedIndex = index >= 0 ? index : 0;
            _suppressSelectionEvents = false;
        }

        public void SetConnectionStatus(string providerDisplayName, bool configured)
        {
            SafeInvoke(() =>
            {
                _statusLabel.Text = providerDisplayName + ": " + (configured ? "Connected" : "Not configured");
                _statusLabel.ForeColor = configured
                    ? Color.FromArgb(100, 200, 100)
                    : Color.FromArgb(255, 120, 120);
            });
        }

        public bool PromptForApproval(string toolName, string argsJson)
        {
            if (InvokeRequired)
                return (bool)Invoke(new Func<bool>(() => PromptForApproval(toolName, argsJson)));

            var dialogText = "Approve SolidWorks action?\n\nTool: " + toolName + "\n\nArguments:\n" + argsJson;
            return MessageBox.Show(
                this,
                dialogText,
                "Approve Tool Call",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes;
        }

        public void AppendUserMessage(string text)
        {
            SafeInvoke(() => AppendColored("\nYou: " + text + "\n", Color.FromArgb(130, 180, 255)));
        }

        public void AppendAssistantMessage(string text)
        {
            SafeInvoke(() => AppendColored("\nAssistant: " + text + "\n", Color.FromArgb(180, 230, 180)));
        }

        public void AppendSystemMessage(string text)
        {
            SafeInvoke(() => AppendColored("\nSystem: " + text + "\n", Color.FromArgb(255, 200, 100)));
        }

        public void AppendToolMessage(string toolName, string argsJson)
        {
            SafeInvoke(() => AppendColored(
                "\nTool: " + toolName + "(" + TruncateArgs(argsJson) + ")\n",
                Color.FromArgb(145, 145, 170)));
        }

        public void AppendApprovalMessage(string toolName, string argsJson)
        {
            SafeInvoke(() => AppendColored(
                "\nApproval required: " + toolName + "(" + TruncateArgs(argsJson) + ")\n",
                Color.FromArgb(255, 170, 120)));
        }

        public void UpdateTokenUsage(int input, int output)
        {
            SafeInvoke(() => _tokenLabel.Text = string.Format("Tokens: {0:N0} in / {1:N0} out", input, output));
        }

        public void SetBusy(bool busy)
        {
            SafeInvoke(() =>
            {
                _sendBtn.Enabled = !busy;
                _inputBox.Enabled = !busy;
                _saveKeyBtn.Enabled = !busy;
                _sendBtn.Text = busy ? "..." : "Send";
            });
        }

        public void ClearChatHistory()
        {
            SafeInvoke(() =>
            {
                _chatBox.Clear();
                AppendColored("Chat cleared.\n", Color.FromArgb(140, 140, 140));
            });
        }

        private void InitializeLayout()
        {
            BackColor = Color.FromArgb(30, 30, 30);
            Dock = DockStyle.Fill;
            Font = new Font("Segoe UI", 9.5f);

            _settingsPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 180,
                BackColor = Color.FromArgb(40, 40, 40),
                Padding = new Padding(8),
                Visible = false
            };

            var providerLabel = NewLabel("Provider:");
            _providerCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                Height = 24
            };
            _providerCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressSelectionEvents) return;
                var item = _providerCombo.SelectedItem as ComboItem;
                if (item != null) OnProviderChanged?.Invoke(item.Value);
            };

            var modelLabel = NewLabel("Model:");
            _modelCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                Height = 24
            };
            _modelCombo.SelectedIndexChanged += (s, e) =>
            {
                if (_suppressSelectionEvents) return;
                if (_modelCombo.SelectedItem != null)
                    OnModelChanged?.Invoke(_modelCombo.SelectedItem.ToString());
            };

            var apiKeyLabel = NewLabel("API Key:");
            _apiKeyBox = new TextBox
            {
                Dock = DockStyle.Top,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.White,
                Height = 24,
                UseSystemPasswordChar = true,
                BorderStyle = BorderStyle.FixedSingle
            };

            _saveKeyBtn = new Button
            {
                Text = "Validate & Save",
                Dock = DockStyle.Top,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.White
            };
            _saveKeyBtn.Click += (s, e) => OnSaveApiKey?.Invoke(_apiKeyBox.Text.Trim());

            _settingsPanel.Controls.Add(_saveKeyBtn);
            _settingsPanel.Controls.Add(_apiKeyBox);
            _settingsPanel.Controls.Add(apiKeyLabel);
            _settingsPanel.Controls.Add(_modelCombo);
            _settingsPanel.Controls.Add(modelLabel);
            _settingsPanel.Controls.Add(_providerCombo);
            _settingsPanel.Controls.Add(providerLabel);

            var topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = Color.FromArgb(35, 35, 35),
                Padding = new Padding(4)
            };

            _settingsToggle = new Button
            {
                Text = "Settings",
                Dock = DockStyle.Left,
                Width = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.FromArgb(210, 210, 210),
                Font = new Font("Segoe UI", 8.5f)
            };
            _settingsToggle.Click += (s, e) => _settingsPanel.Visible = !_settingsPanel.Visible;

            _statusLabel = new Label
            {
                Text = "Not configured",
                ForeColor = Color.FromArgb(255, 120, 120),
                Dock = DockStyle.Right,
                Width = 190,
                TextAlign = ContentAlignment.MiddleRight,
                Font = new Font("Segoe UI", 8f)
            };

            topBar.Controls.Add(_statusLabel);
            topBar.Controls.Add(_settingsToggle);

            _chatBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(25, 25, 25),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9.25f),
                WordWrap = true
            };

            var bottomPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 78,
                BackColor = Color.FromArgb(35, 35, 35),
                Padding = new Padding(4)
            };

            var btnPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28
            };

            _sendBtn = new Button
            {
                Text = "Send",
                Dock = DockStyle.Right,
                Width = 70,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(80, 120, 200),
                ForeColor = Color.White
            };
            _sendBtn.Click += (s, e) =>
            {
                OnSendMessage?.Invoke(_inputBox.Text.Trim());
                _inputBox.Clear();
            };

            _clearBtn = new Button
            {
                Text = "Clear",
                Dock = DockStyle.Right,
                Width = 56,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(60, 60, 60),
                ForeColor = Color.FromArgb(180, 180, 180)
            };
            _clearBtn.Click += (s, e) => OnClearChat?.Invoke();

            _tokenLabel = new Label
            {
                Text = "Tokens: 0 in / 0 out",
                ForeColor = Color.FromArgb(140, 140, 140),
                Dock = DockStyle.Left,
                Width = 180,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 7.5f)
            };

            btnPanel.Controls.Add(_sendBtn);
            btnPanel.Controls.Add(_clearBtn);
            btnPanel.Controls.Add(_tokenLabel);

            _inputBox = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(45, 45, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Multiline = true,
                Font = new Font("Segoe UI", 9.5f)
            };
            _inputBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter && !e.Shift)
                {
                    e.SuppressKeyPress = true;
                    OnSendMessage?.Invoke(_inputBox.Text.Trim());
                    _inputBox.Clear();
                }
            };

            bottomPanel.Controls.Add(_inputBox);
            bottomPanel.Controls.Add(btnPanel);

            Controls.Add(_chatBox);
            Controls.Add(_settingsPanel);
            Controls.Add(topBar);
            Controls.Add(bottomPanel);
        }

        private static Label NewLabel(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Color.FromArgb(200, 200, 200),
                Dock = DockStyle.Top,
                Height = 22
            };
        }

        private void AppendColored(string text, Color color)
        {
            _chatBox.SelectionStart = _chatBox.TextLength;
            _chatBox.SelectionLength = 0;
            _chatBox.SelectionColor = color;
            _chatBox.AppendText(text);
            _chatBox.ScrollToCaret();
        }

        private void SafeInvoke(Action action)
        {
            if (InvokeRequired)
                BeginInvoke(action);
            else
                action();
        }

        private static string TruncateArgs(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "{}";
            return json.Length > 120 ? json.Substring(0, 117) + "..." : json;
        }
    }
}
