using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;

namespace WinAI.Services
{
    /// <summary>
    /// Manages secure credential storage for AI provider API keys using 
    /// Windows Credential Locker (PasswordVault) and local encrypted vault security.
    /// </summary>
    public sealed class CredentialVaultService
    {
        private const string VaultResourceName = "WinAI_KeyVault";
        private const string VaultPinHashKey = "Vault_PinHash";
        private const string VaultPinSaltKey = "Vault_PinSalt";

        private static readonly Lazy<CredentialVaultService> _instance = 
            new Lazy<CredentialVaultService>(() => new CredentialVaultService());

        public static CredentialVaultService Instance => _instance.Value;

        private readonly PasswordVault _vault;
        private readonly ApplicationDataContainer _settings;
        private bool _isUnlockedSession;

        public event EventHandler VaultStateChanged;

        private CredentialVaultService()
        {
            _vault = new PasswordVault();
            _settings = ApplicationData.Current.LocalSettings;
            MigrateLegacyKeys();
        }

        #region API Key Operations

        public void SaveKey(string providerId, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return;

            string id = providerId.Trim().ToLowerInvariant();
            string key = apiKey?.Trim() ?? string.Empty;

            try
            {
                // Remove existing if present
                RemoveKey(id);

                if (!string.IsNullOrEmpty(key))
                {
                    var credential = new PasswordCredential(VaultResourceName, id, key);
                    _vault.Add(credential);
                }
            }
            catch
            {
                // Fallback safe handling
            }

            VaultStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public string GetKey(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return string.Empty;

            string id = providerId.Trim().ToLowerInvariant();

            try
            {
                var cred = _vault.Retrieve(VaultResourceName, id);
                if (cred != null)
                {
                    cred.RetrievePassword();
                    return cred.Password ?? string.Empty;
                }
            }
            catch
            {
                // Credential not found in vault
            }

            return string.Empty;
        }

        public bool HasKey(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return false;

            string id = providerId.Trim().ToLowerInvariant();

            try
            {
                var list = _vault.FindAllByResource(VaultResourceName);
                if (list != null)
                {
                    return list.Any(c => c.UserName.Equals(id, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
                // No credentials found
            }

            return false;
        }

        public void RemoveKey(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId)) return;

            string id = providerId.Trim().ToLowerInvariant();

            try
            {
                var cred = _vault.Retrieve(VaultResourceName, id);
                if (cred != null)
                {
                    _vault.Remove(cred);
                }
            }
            catch
            {
                // Not found
            }

            VaultStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ClearAllKeys()
        {
            try
            {
                var list = _vault.FindAllByResource(VaultResourceName);
                if (list != null)
                {
                    foreach (var cred in list)
                    {
                        _vault.Remove(cred);
                    }
                }
            }
            catch
            {
                // Ignore if empty
            }

            VaultStateChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Aliases and Convenience Methods

        public bool HasApiKey(string providerId) => HasKey(providerId);
        public string GetApiKey(string providerId) => GetKey(providerId);
        public void SaveApiKey(string providerId, string apiKey) => SaveKey(providerId, apiKey);
        public void DeleteApiKey(string providerId) => RemoveKey(providerId);
        public bool IsVaultLocked() => IsLocked;
        public bool VerifyPin(string pin) => VerifyPinInternal(pin);

        #endregion

        #region Vault PIN & Lock Protection

        public bool HasPin => _settings.Values.ContainsKey(VaultPinHashKey);

        public bool IsLocked => HasPin && !_isUnlockedSession;

        public void LockVault()
        {
            _isUnlockedSession = false;
            VaultStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool UnlockVault(string pin)
        {
            if (!HasPin)
            {
                _isUnlockedSession = true;
                VaultStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            if (VerifyPinInternal(pin))
            {
                _isUnlockedSession = true;
                VaultStateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }

            return false;
        }

        public bool SetPin(string pin)
        {
            if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4) return false;

            string salt = Guid.NewGuid().ToString("N");
            string hash = ComputeHash(pin, salt);

            _settings.Values[VaultPinSaltKey] = salt;
            _settings.Values[VaultPinHashKey] = hash;
            _isUnlockedSession = true;

            VaultStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void RemovePin()
        {
            _settings.Values.Remove(VaultPinSaltKey);
            _settings.Values.Remove(VaultPinHashKey);
            _isUnlockedSession = true;

            VaultStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool VerifyPinInternal(string pin)
        {
            if (string.IsNullOrEmpty(pin)) return false;

            if (_settings.Values.TryGetValue(VaultPinHashKey, out object hashObj) &&
                _settings.Values.TryGetValue(VaultPinSaltKey, out object saltObj) &&
                hashObj is string expectedHash &&
                saltObj is string salt)
            {
                string computed = ComputeHash(pin, salt);
                return string.Equals(expectedHash, computed, StringComparison.Ordinal);
            }

            return false;
        }

        private string ComputeHash(string input, string salt)
        {
            try
            {
                var algorithm = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256);
                var buffer = CryptographicBuffer.ConvertStringToBinary(input + salt, BinaryStringEncoding.Utf8);
                var hashBuffer = algorithm.HashData(buffer);
                return CryptographicBuffer.EncodeToHexString(hashBuffer);
            }
            catch
            {
                return (input + salt).GetHashCode().ToString("X8");
            }
        }

        #endregion

        #region Migration from LocalSettings

        private void MigrateLegacyKeys()
        {
            try
            {
                var mapping = new Dictionary<string, string>
                {
                    { ApiKeyService.KeyOpenAI, "openai" },
                    { ApiKeyService.KeyGemini, "gemini" },
                    { ApiKeyService.KeyClaude, "claude" },
                    { ApiKeyService.KeyDeepSeek, "deepseek" },
                    { ApiKeyService.KeyPerplexity, "perplexity" },
                    { ApiKeyService.KeyXAI, "xai" },
                    { ApiKeyService.KeyGroq, "groq" },
                    { ApiKeyService.KeyMistral, "mistral" },
                    { ApiKeyService.KeyOpenRouter, "openrouter" }
                };

                foreach (var kvp in mapping)
                {
                    if (_settings.Values.TryGetValue(kvp.Key, out object val) && val is string keyStr && !string.IsNullOrWhiteSpace(keyStr))
                    {
                        if (!HasKey(kvp.Value))
                        {
                            SaveKey(kvp.Value, keyStr);
                        }
                        // Remove from unencrypted local settings
                        _settings.Values.Remove(kvp.Key);
                    }
                }
            }
            catch
            {
                // Migration safeguard
            }
        }

        #endregion
    }
}
