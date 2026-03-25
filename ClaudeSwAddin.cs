using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ClaudeSW.Api;
using ClaudeSW.Core;
using ClaudeSW.Security;
using ClaudeSW.Tools;
using ClaudeSW.UI;
using Microsoft.Win32;
using SolidWorks.Interop.swpublished;

namespace ClaudeSW
{
    [ComVisible(true)]
    [Guid("8A2B3C4D-5E6F-7A8B-9C0D-E1F2A3B4C5D6")]
    [DisplayName("SolidWorks AI Assistant")]
    [Description("Provider-agnostic AI assistant for SolidWorks")]
    public class ClaudeSwAddin : ISwAddin
    {
        private const string AddinKeyTemplate = @"SOFTWARE\SolidWorks\Addins\{{{0}}}";
        private const string StartupKeyTemplate = @"Software\SolidWorks\AddInsStartup\{{{0}}}";
        private const string TitleRegKey = "Title";
        private const string DescRegKey = "Description";

        private dynamic _swApp;
        private SwThreadMarshaller _marshaller;
        private SwToolExecutor _toolExecutor;
        private ModelProviderRegistry _providers;
        private AgentSessionOrchestrator _orchestrator;
        private ChatTaskPane _taskPane;

        [ComRegisterFunction]
        public static void RegisterFunction(Type t)
        {
            try
            {
                string guid = t.GUID.ToString();
                using (var key = Registry.LocalMachine.CreateSubKey(string.Format(AddinKeyTemplate, guid)))
                {
                    key.SetValue(TitleRegKey, GetDisplayName(t));
                    key.SetValue(DescRegKey, GetDescription(t));
                }

                using (var key = Registry.CurrentUser.CreateSubKey(string.Format(StartupKeyTemplate, guid)))
                {
                    key.SetValue(null, 1);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ClaudeSW COM registration failed: " + ex.Message);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                string guid = t.GUID.ToString();
                Registry.LocalMachine.DeleteSubKey(string.Format(AddinKeyTemplate, guid), false);
                Registry.CurrentUser.DeleteSubKey(string.Format(StartupKeyTemplate, guid), false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ClaudeSW COM unregistration failed: " + ex.Message);
            }
        }

        public bool ConnectToSW(object thisSW, int cookie)
        {
            try
            {
                _swApp = thisSW;
                _marshaller = new SwThreadMarshaller();
                _toolExecutor = new SwToolExecutor(_swApp, _marshaller.InvokeAsync);
                _providers = new ModelProviderRegistry(new IModelProvider[]
                {
                    new OpenAIModelProvider(),
                    new AnthropicModelProvider()
                }, "openai");

                foreach (var provider in _providers.GetProviderDescriptors())
                {
                    if (!CredentialStore.HasApiKey(provider.Key))
                        continue;

                    try
                    {
                        _providers.SetApiKey(provider.Key, CredentialStore.LoadApiKey(provider.Key));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Failed to load credential for " + provider.Key + ": " + ex.Message);
                    }
                }

                _orchestrator = new AgentSessionOrchestrator(_providers);
                _taskPane = new ChatTaskPane(_swApp, _orchestrator, _toolExecutor);
                _taskPane.CreatePane();

                Debug.WriteLine("SolidWorks AI Assistant loaded successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ClaudeSW ConnectToSW failed: " + ex);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            try
            {
                _taskPane?.Dispose();
                _providers?.Dispose();
                _marshaller?.Dispose();
                _swApp = null;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetDisplayName(Type t)
        {
            var attr = t.GetCustomAttributes(typeof(DisplayNameAttribute), false)
                .OfType<DisplayNameAttribute>()
                .FirstOrDefault();
            return attr != null ? attr.DisplayName : t.Name;
        }

        private static string GetDescription(Type t)
        {
            var attr = t.GetCustomAttributes(typeof(DescriptionAttribute), false)
                .OfType<DescriptionAttribute>()
                .FirstOrDefault();
            return attr != null ? attr.Description : "";
        }
    }
}
