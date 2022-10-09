using Azure.Security.KeyVault.Secrets;
using Czf.Domain.NewsBankWrapper.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArchiveBot.Core.Maui
{
    public class SecretClientEZProxySignInUriProvider : IEZProxySignInUriProvider
    {
        private readonly SecretClient _secretClient;

        public SecretClientEZProxySignInUriProvider(SecretClient secretClient)
        {
            _secretClient = secretClient;
        }

        public Uri GetSignInUri()
        {
            return new Uri(_secretClient.GetSecret("EZProxySignInUri").Value.Value);
        }
    }
}
