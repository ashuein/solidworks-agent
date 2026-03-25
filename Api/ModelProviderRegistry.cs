using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ClaudeSW.Api
{
    public class ModelProviderRegistry : IDisposable
    {
        private readonly Dictionary<string, IModelProvider> _providers;
        private string _currentProviderKey;
        private string _currentModel;

        public ModelProviderRegistry(IEnumerable<IModelProvider> providers, string defaultProviderKey)
        {
            _providers = providers.ToDictionary(p => p.Descriptor.Key, StringComparer.OrdinalIgnoreCase);
            if (!_providers.ContainsKey(defaultProviderKey))
                throw new ArgumentException("Unknown default provider: " + defaultProviderKey);

            SetCurrentProvider(defaultProviderKey);
        }

        public IEnumerable<ProviderDescriptor> GetProviderDescriptors()
        {
            return _providers.Values.Select(p => p.Descriptor).OrderBy(p => p.DisplayName).ToList();
        }

        public string CurrentProviderKey
        {
            get { return _currentProviderKey; }
        }

        public string CurrentModel
        {
            get { return _currentModel; }
        }

        public IModelProvider CurrentProvider
        {
            get { return _providers[_currentProviderKey]; }
        }

        public ProviderDescriptor CurrentProviderDescriptor
        {
            get { return CurrentProvider.Descriptor; }
        }

        public bool IsConfigured
        {
            get { return CurrentProvider.IsConfigured; }
        }

        public void SetCurrentProvider(string providerKey)
        {
            if (!_providers.ContainsKey(providerKey))
                throw new ArgumentException("Unknown provider: " + providerKey);

            _currentProviderKey = providerKey;
            _currentModel = _providers[providerKey].Descriptor.DefaultModel;
        }

        public void SetCurrentModel(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("Model cannot be empty.");

            _currentModel = model;
        }

        public void SetApiKey(string providerKey, string apiKey)
        {
            if (!_providers.ContainsKey(providerKey))
                throw new ArgumentException("Unknown provider: " + providerKey);

            _providers[providerKey].SetApiKey(apiKey);
        }

        public Task<string> ValidateCurrentProviderAsync(CancellationToken ct)
        {
            return CurrentProvider.ValidateKeyAsync(_currentModel, ct);
        }

        public Task<ProviderTurnResponse> GenerateTurnAsync(ProviderTurnRequest request, CancellationToken ct)
        {
            request.Model = _currentModel;
            return CurrentProvider.GenerateTurnAsync(request, ct);
        }

        public void Dispose()
        {
            foreach (var provider in _providers.Values)
                provider.Dispose();
        }
    }
}
