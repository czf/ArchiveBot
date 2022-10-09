using Czf.Domain.NewsBankWrapper.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.Security.KeyVault.Secrets;

namespace ArchiveBot.Core.Maui
{
    public class SecretClientEZProxySignInCredentialProvider : IEZProxySignInCredentialsProvider
    {
        private readonly SecretClient _secretClient;
        public SecretClientEZProxySignInCredentialProvider(SecretClient secretClient)
        {
            _secretClient = secretClient;
        }
        public string GetAccount() => _secretClient.GetSecret("EZProxyAccount").Value.Value;

        public string GetPassword() => _secretClient.GetSecret("EZProxyPassword").Value.Value;
    }
}
