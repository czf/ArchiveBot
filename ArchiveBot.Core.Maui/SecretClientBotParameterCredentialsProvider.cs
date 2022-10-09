using ArchiveBot.Core.Objects;
using Azure.Security.KeyVault.Secrets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArchiveBot.Core.Maui
{
    public class SecretClientBotParameterCredentialsProvider : IBotParameterCredentialsProvider
    {
        private readonly SecretClient _secretClient;

        public SecretClientBotParameterCredentialsProvider(SecretClient secretClient)
        {
            _secretClient = secretClient;
        }

        public string BotName => _secretClient.GetSecret(nameof(BotName)).Value.Value;

        public string BotPassword => _secretClient.GetSecret(nameof(BotPassword)).Value.Value;

        public string BotSecret => _secretClient.GetSecret(nameof(BotSecret)).Value.Value;

        public string BotClientId => _secretClient.GetSecret(nameof(BotClientId)).Value.Value;
    }
}
