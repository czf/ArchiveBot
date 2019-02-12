using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Czf.Domain.NewsBankWrapper.Interfaces;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Microsoft.Azure.Services.AppAuthentication;

namespace ArchiveBot.Objects.NewsBankDependancies
{
    public class AzureKeyVaultEZProxySignInCredentialsProvider : IEZProxySignInCredentialsProvider
    {
        private KeyVaultClient _keyVaultClient;
        public AzureKeyVaultEZProxySignInCredentialsProvider()
        {
            AzureServiceTokenProvider azureServiceTokenProvider = new AzureServiceTokenProvider();
            _keyVaultClient = new KeyVaultClient(
                new KeyVaultClient.AuthenticationCallback(
                    azureServiceTokenProvider.KeyVaultTokenCallback));
        }

        public string GetAccount()
        {
            SecretBundle bundle = _keyVaultClient.GetSecretAsync(Environment.GetEnvironmentVariable("AzureKeyVaultEzProxyAccountSecretId")).Result;
            return bundle.Value;
        }

        public string GetPassword()
        {
            return _keyVaultClient.GetSecretAsync(Environment.GetEnvironmentVariable("AzureKeyVaultEzProxyPasswordSecretId")).Result.Value;
        }
    }
}
