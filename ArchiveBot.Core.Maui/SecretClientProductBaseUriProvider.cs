using Azure.Security.KeyVault.Secrets;
using Czf.Domain.NewsBankWrapper.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArchiveBot.Core.Maui
{
    public class SecretClientProductBaseUriProvider : IProductBaseUriProvider
    {
        private readonly SecretClient _secretClient;

        public SecretClientProductBaseUriProvider(SecretClient secretClient)
        {
            _secretClient = secretClient;
        }

        public Uri GetProductBaseUri()
        {
            return new Uri(_secretClient.GetSecret("ProductBaseUri").Value.Value);
        }
    }
}
